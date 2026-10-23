using System.Text.Json;
using OpenCvSharp;

namespace TrafficCounter.Vision;

public enum DriftStatus : byte
{
    /// <summary>Too few matching features (night, fog, occluded zones) – no statement possible.</summary>
    Unreliable = 0,
    Ok = 1,

    /// <summary>Small drift: counting still works, stage 2 will compensate it.</summary>
    Shifted = 2,

    /// <summary>Large movement: calibration no longer valid.</summary>
    Moved = 3,
}

/// <summary>Estimated camera movement relative to the calibration (ADR-0019).</summary>
public sealed record DriftMeasurement(
    DriftStatus Status,
    double DxPixels,
    double DyPixels,
    double RotationDegrees,
    double Scale,
    int Matches,
    double InlierRatio)
{
    public double ShiftPixels => Math.Sqrt((DxPixels * DxPixels) + (DyPixels * DyPixels));
}

/// <summary>ORB features of the static zones at calibration time. Contains no image (ADR-0005).</summary>
public sealed class DriftReference
{
    public int ImageWidth { get; set; }

    public int ImageHeight { get; set; }

    /// <summary>Static zones in pixels of the reference image.</summary>
    public List<int[]> Zones { get; set; } = [];

    /// <summary>Keypoint positions as x, y pairs.</summary>
    public List<float[]> Points { get; set; } = [];

    /// <summary>32-byte ORB descriptor per keypoint, Base64.</summary>
    public List<string> Descriptors { get; set; } = [];

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this));

    public static DriftReference? Load(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<DriftReference>(File.ReadAllText(path)) : null;

    /// <summary>Conventional location next to the site configuration: "site.json" → "site.drift.json".</summary>
    public static string PathFor(string siteConfigPath) => Path.ChangeExtension(siteConfigPath, ".drift.json");
}

public sealed class DriftSettings
{
    /// <summary>Shift (fraction of the image width) up to which the camera counts as unchanged.</summary>
    public double OkFraction { get; set; } = 0.004;

    /// <summary>Shift (fraction of the image width) from which the calibration counts as invalid.</summary>
    public double MovedFraction { get; set; } = 0.02;

    public double MovedRotationDegrees { get; set; } = 1.0;

    public int MinInliers { get; set; } = 12;

    public double MinInlierRatio { get; set; } = 0.25;

    /// <summary>
    /// Largest plausible scale change. A fixed camera cannot zoom; an estimate beyond this matched the wrong features,
    /// e.g. at dusk a scale near 0 with a "rotation" of 60–90°, and is unreliable rather than a movement.
    /// </summary>
    public double MaxScaleChange { get; set; } = 0.1;
}

/// <summary>
/// Compares ORB features in static zones (facades, poles, kerbs) with the calibration reference and estimates
/// shift, rotation and scale with RANSAC (ADR-0019).
/// </summary>
public static class DriftMonitor
{
    private const int Features = 1500;
    private const double RatioTest = 0.75;

    public static DriftReference CreateReference(Mat image, IEnumerable<Rect> zones)
    {
        List<Rect> zoneList = [.. zones];
        using Mat gray = Gray(image);
        using Mat mask = Mask(gray.Size(), zoneList, grow: 0);
        (KeyPoint[] keypoints, Mat descriptors) = Detect(gray, mask);
        using (descriptors)
        {
            var reference = new DriftReference
            {
                ImageWidth = image.Width,
                ImageHeight = image.Height,
                Zones = [.. zoneList.Select(z => new[] { z.X, z.Y, z.Width, z.Height })],
            };
            byte[] all = [];
            if (!descriptors.Empty())
            {
                descriptors.GetArray(out all);
            }

            int size = descriptors.Empty() ? 0 : descriptors.Cols;
            for (int i = 0; i < keypoints.Length && size > 0; i++)
            {
                reference.Points.Add([keypoints[i].Pt.X, keypoints[i].Pt.Y]);
                reference.Descriptors.Add(Convert.ToBase64String(all, i * size, size));
            }

            return reference;
        }
    }

    public static DriftMeasurement Measure(Mat image, DriftReference reference, DriftSettings? settings = null)
    {
        settings ??= new DriftSettings();
        if (reference.Points.Count < settings.MinInliers || image.Width != reference.ImageWidth || image.Height != reference.ImageHeight)
        {
            return new DriftMeasurement(DriftStatus.Unreliable, 0, 0, 0, 1, 0, 0);
        }

        using Mat gray = Gray(image);
        List<Rect> zones = [.. reference.Zones.Select(z => new Rect(z[0], z[1], z[2], z[3]))];
        int grow = (int)Math.Ceiling(settings.MovedFraction * image.Width * 2); // search a bit beyond the zones
        using Mat mask = Mask(gray.Size(), zones, grow);
        (KeyPoint[] keypoints, Mat descriptors) = Detect(gray, mask);
        using (descriptors)
        using (Mat referenceDescriptors = Descriptors(reference))
        {
            if (keypoints.Length < settings.MinInliers)
            {
                return new DriftMeasurement(DriftStatus.Unreliable, 0, 0, 0, 1, keypoints.Length, 0);
            }

            using var matcher = new BFMatcher(NormTypes.Hamming);
            DMatch[][] candidates = matcher.KnnMatch(referenceDescriptors, descriptors, k: 2);
            List<DMatch> good = [.. candidates.Where(c => c.Length == 2 && c[0].Distance < RatioTest * c[1].Distance).Select(c => c[0])];
            if (good.Count < settings.MinInliers)
            {
                return new DriftMeasurement(DriftStatus.Unreliable, 0, 0, 0, 1, good.Count, 0);
            }

            Point2f[] from = [.. good.Select(m => new Point2f(reference.Points[m.QueryIdx][0], reference.Points[m.QueryIdx][1]))];
            Point2f[] to = [.. good.Select(m => keypoints[m.TrainIdx].Pt)];
            using var inliers = new Mat();
            using Mat? transform = Cv2.EstimateAffinePartial2D(
                InputArray.Create(from), InputArray.Create(to), inliers, RobustEstimationAlgorithms.RANSAC, ransacReprojThreshold: 3);
            int inlierCount = inliers.Empty() ? 0 : Cv2.CountNonZero(inliers);
            double inlierRatio = (double)inlierCount / good.Count;
            if (transform is null || transform.Empty() || inlierCount < settings.MinInliers || inlierRatio < settings.MinInlierRatio)
            {
                return new DriftMeasurement(DriftStatus.Unreliable, 0, 0, 0, 1, good.Count, inlierRatio);
            }

            double a = transform.At<double>(0, 0);
            double b = transform.At<double>(1, 0);
            double dx = transform.At<double>(0, 2);
            double dy = transform.At<double>(1, 2);
            double scale = Math.Sqrt((a * a) + (b * b));
            double rotation = Math.Atan2(b, a) * 180 / Math.PI;
            if (Math.Abs(scale - 1) > settings.MaxScaleChange)
            {
                return new DriftMeasurement(DriftStatus.Unreliable, 0, 0, 0, scale, good.Count, inlierRatio);
            }

            // Movement of the image centre captures rotation and scale around it, too.
            double cx = image.Width / 2.0;
            double cy = image.Height / 2.0;
            double centreDx = ((a * cx) - (b * cy) + dx) - cx;
            double centreDy = ((b * cx) + (a * cy) + dy) - cy;
            double shift = Math.Sqrt((centreDx * centreDx) + (centreDy * centreDy));

            DriftStatus status = shift > settings.MovedFraction * image.Width || Math.Abs(rotation) > settings.MovedRotationDegrees
                ? DriftStatus.Moved
                : shift > settings.OkFraction * image.Width ? DriftStatus.Shifted : DriftStatus.Ok;
            return new DriftMeasurement(status, centreDx, centreDy, rotation, scale, good.Count, inlierRatio);
        }
    }

    private static Mat Gray(Mat image)
    {
        var gray = new Mat();
        if (image.Channels() == 1)
        {
            image.CopyTo(gray);
        }
        else
        {
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        }

        return gray;
    }

    private static Mat Mask(Size size, IEnumerable<Rect> zones, int grow)
    {
        var mask = new Mat(size, MatType.CV_8UC1, Scalar.All(0));
        var bounds = new Rect(0, 0, size.Width, size.Height);
        foreach (Rect zone in zones)
        {
            Rect grown = new Rect(zone.X - grow, zone.Y - grow, zone.Width + (2 * grow), zone.Height + (2 * grow)) & bounds;
            mask[grown].SetTo(Scalar.All(255));
        }

        return mask;
    }

    private static (KeyPoint[] Keypoints, Mat Descriptors) Detect(Mat gray, Mat mask)
    {
        using var orb = ORB.Create(Features);
        var descriptors = new Mat();
        orb.DetectAndCompute(gray, mask, out KeyPoint[] keypoints, descriptors);
        return (keypoints, descriptors);
    }

    private static Mat Descriptors(DriftReference reference)
    {
        byte[] all = [.. reference.Descriptors.SelectMany(Convert.FromBase64String)];
        var matrix = new Mat(reference.Descriptors.Count, all.Length / Math.Max(1, reference.Descriptors.Count), MatType.CV_8UC1);
        matrix.SetArray(all);
        return matrix;
    }
}
