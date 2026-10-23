using OpenCvSharp;

namespace TrafficCounter.Vision.Tests;

public class DriftMonitorTests
{
    private const int Width = 1280;
    private const int Height = 720;

    // Two "facades" above the road, where features are searched.
    private static readonly Rect[] Zones = [new Rect(100, 40, 400, 200), new Rect(760, 60, 420, 220)];

    /// <summary>Synthetic scene with plenty of corners (windows, bricks) and a featureless road.</summary>
    private static Mat Scene()
    {
        var image = new Mat(Height, Width, MatType.CV_8UC3, new Scalar(90, 110, 120));
        var random = new Random(7);
        for (int i = 0; i < 400; i++)
        {
            var box = new Rect(random.Next(0, Width - 40), random.Next(0, 320), random.Next(8, 40), random.Next(8, 40));
            Cv2.Rectangle(image, box, Scalar.All(random.Next(0, 255)), -1);
        }

        Cv2.Rectangle(image, new Rect(0, 400, Width, 250), new Scalar(60, 60, 60), -1); // road
        return image;
    }

    private static Mat Transform(Mat image, double dx, double dy, double degrees = 0, double scale = 1)
    {
        using Mat matrix = Cv2.GetRotationMatrix2D(new Point2f(Width / 2f, Height / 2f), degrees, scale);
        matrix.Set(0, 2, matrix.At<double>(0, 2) + dx);
        matrix.Set(1, 2, matrix.At<double>(1, 2) + dy);
        var moved = new Mat();
        Cv2.WarpAffine(image, moved, matrix, image.Size(), InterpolationFlags.Linear, BorderTypes.Replicate);
        return moved;
    }

    [Fact]
    public void Unchanged_camera_is_ok()
    {
        using Mat scene = Scene();
        DriftReference reference = DriftMonitor.CreateReference(scene, Zones);

        DriftMeasurement result = DriftMonitor.Measure(scene, reference);

        Assert.Equal(DriftStatus.Ok, result.Status);
        Assert.True(result.ShiftPixels < 0.5);
        Assert.True(result.Matches > 50);
    }

    [Fact]
    public void Small_shift_is_measured_and_reported_as_shifted()
    {
        using Mat scene = Scene();
        DriftReference reference = DriftMonitor.CreateReference(scene, Zones);
        using Mat moved = Transform(scene, dx: 12, dy: -7);

        DriftMeasurement result = DriftMonitor.Measure(moved, reference);

        Assert.Equal(DriftStatus.Shifted, result.Status);
        Assert.Equal(12, result.DxPixels, 1.0);
        Assert.Equal(-7, result.DyPixels, 1.0);
    }

    [Fact]
    public void Rotation_is_measured()
    {
        using Mat scene = Scene();
        DriftReference reference = DriftMonitor.CreateReference(scene, Zones);
        using Mat moved = Transform(scene, 0, 0, degrees: 0.6);

        DriftMeasurement result = DriftMonitor.Measure(moved, reference);

        // OpenCV rotates counter-clockwise for positive angles, which is -0.6° in image coordinates (y down).
        Assert.Equal(-0.6, result.RotationDegrees, 0.1);
    }

    [Fact]
    public void Large_movement_invalidates_the_calibration()
    {
        using Mat scene = Scene();
        DriftReference reference = DriftMonitor.CreateReference(scene, Zones);
        using Mat moved = Transform(scene, dx: 45, dy: 10);

        Assert.Equal(DriftStatus.Moved, DriftMonitor.Measure(moved, reference).Status);
    }

    [Fact]
    public void Implausible_scale_is_unreliable_not_a_movement()
    {
        // A fixed camera cannot zoom; such an estimate matched the wrong features (seen at dusk as scale ~0, "90°").
        using Mat scene = Scene();
        DriftReference reference = DriftMonitor.CreateReference(scene, Zones);
        using Mat zoomed = Transform(scene, 0, 0, scale: 0.7);

        DriftMeasurement result = DriftMonitor.Measure(zoomed, reference);

        Assert.Equal(DriftStatus.Unreliable, result.Status);
        Assert.Equal(0.7, result.Scale, 0.05); // rejected for the scale, not for lack of matches
    }

    [Fact]
    public void Featureless_image_is_unreliable()
    {
        using Mat scene = Scene();
        DriftReference reference = DriftMonitor.CreateReference(scene, Zones);
        using var fog = new Mat(Height, Width, MatType.CV_8UC3, Scalar.All(128));

        Assert.Equal(DriftStatus.Unreliable, DriftMonitor.Measure(fog, reference).Status);
    }

    [Fact]
    public void Reference_round_trips_through_file_without_image_data()
    {
        using Mat scene = Scene();
        DriftReference reference = DriftMonitor.CreateReference(scene, Zones);
        string path = Path.Combine(Path.GetTempPath(), $"drift-{Guid.NewGuid():N}.json");
        try
        {
            reference.Save(path);
            DriftReference loaded = DriftReference.Load(path)!;

            Assert.Equal(reference.Points.Count, loaded.Points.Count);
            Assert.Equal(DriftStatus.Ok, DriftMonitor.Measure(scene, loaded).Status);
            Assert.True(new FileInfo(path).Length < 200_000);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reference_path_sits_next_to_the_site_configuration() =>
        Assert.Equal(Path.Combine("cfg", "site.drift.json"), DriftReference.PathFor(Path.Combine("cfg", "site.json")));
}
