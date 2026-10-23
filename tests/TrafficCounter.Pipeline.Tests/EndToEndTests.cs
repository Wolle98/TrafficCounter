using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;
using TrafficCounter.Evaluation;
using TrafficCounter.Tracking;
using TrafficCounter.Vision;

namespace TrafficCounter.Pipeline.Tests;

/// <summary>
/// Recorded video → file source → detection → tracking → counting → evaluation against ground truth,
/// with a synthetic street and a contour-based stand-in for the YOLO detector.
/// </summary>
public sealed class EndToEndTests : IDisposable
{
    private const int Width = 960;
    private const int Height = 540;
    private const double Fps = 25;
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 18, 0, 0, TimeSpan.FromHours(2));

    private readonly string _directory = Directory.CreateTempSubdirectory("tc-e2e-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static SiteOptions Site() => new()
    {
        BandTop = 0.3f,
        BandBottom = 0.9f,
        LineStart = new RelativePoint { X = 0.5f, Y = 0.35f }, // far kerb
        LineEnd = new RelativePoint { X = 0.5f, Y = 0.85f },   // near kerb
    };

    /// <summary>Near lane: vehicles move right (direction B); far lane: they move left (A).</summary>
    private sealed record Vehicle(double StartSecond, bool MovesRight, int Width = 120, int Height = 60, double Speed = 300);

    private string RecordStreet(IReadOnlyList<Vehicle> vehicles, double seconds)
    {
        string path = Path.Combine(_directory, "street.mp4");
        using var recorder = new VideoRecorder(path, new Size(Width, Height), Fps);
        int frames = (int)(seconds * Fps);
        for (int i = 0; i < frames; i++)
        {
            double t = i / Fps;
            using var image = new Mat(Height, Width, MatType.CV_8UC3, new Scalar(40, 40, 40));
            Cv2.Rectangle(image, new Rect(0, 200, Width, 260), new Scalar(70, 70, 70), -1); // road
            foreach (Vehicle v in vehicles.Where(v => t >= v.StartSecond))
            {
                double travelled = (t - v.StartSecond) * v.Speed;
                int x = v.MovesRight ? (int)(-v.Width + travelled) : (int)(Width - travelled);
                int bottom = v.MovesRight ? 440 : 330;
                Cv2.Rectangle(image, new Rect(x, bottom - v.Height, v.Width, v.Height), Scalar.All(230), -1);
            }

            using var frame = new Frame(image.Clone(), T0 + TimeSpan.FromSeconds(t), i + 1);
            recorder.Write(frame);
        }

        return path;
    }

    [Fact]
    public async Task Recorded_video_is_counted_correctly()
    {
        Vehicle[] vehicles =
        [
            new(0.0, MovesRight: true),
            new(0.5, MovesRight: false),
            new(3.0, MovesRight: true, Width: 200, Height: 80),
            new(4.0, MovesRight: false, Speed: 450),
            new(6.0, MovesRight: true, Width: 60, Height: 50, Speed: 150), // cyclist
        ];
        string path = RecordStreet(vehicles, seconds: 12);

        // Ground truth: time when each vehicle's centre passes the middle of the image.
        List<GroundTruthEntry> truth =
        [
            .. vehicles.Select(v => new GroundTruthEntry(
                TimeSpan.FromSeconds(v.StartSecond + (((Width / 2.0) + (v.MovesRight ? v.Width / 2.0 : -v.Width / 2.0)) / v.Speed)),
                v.MovesRight ? ImageMotion.LeftToRight : ImageMotion.RightToLeft)),
        ];

        SiteOptions site = Site();
        using var detector = new BrightBlobDetector();
        var pipeline = new CountingPipeline(detector, new ByteTracker(), site);
        List<DetectedPassage> detected = [];
        DateTimeOffset? first = null;
        await using (var source = new FileFrameSource(path, FilePlayback.Sequential))
        {
            while (source.TryRead(TimeSpan.Zero, out Frame? frame))
            {
                using (frame)
                {
                    first ??= frame!.Timestamp;
                    detected.AddRange(pipeline.Process(frame!).Passages.Select(p => new DetectedPassage(p.PassedAt - first.Value, p.Direction, p.Class)));
                }
            }
        }

        detected.AddRange(pipeline.Flush().Select(p => new DetectedPassage(p.PassedAt - first!.Value, p.Direction, p.Class)));

        LineSegment line = site.Line(new Size(Width, Height));
        EvaluationResult result = CountEvaluator.Evaluate(truth, m => DirectionMapping.ToDirection(line, m), detected, TimeSpan.FromSeconds(0.5));

        Assert.Equal(5, result.Matched);
        Assert.Equal(0, result.Spurious);
        Assert.Equal(0, result.DirectionErrors);
    }

    /// <summary>Stand-in for YOLO: bright blobs inside the band are "cars".</summary>
    private sealed class BrightBlobDetector : IObjectDetector
    {
        public string ModelName => "bright-blobs";

        public string ExecutionProvider => "CPU";

        public IReadOnlyList<Detection> Detect(Mat image, Rect region)
        {
            using Mat roi = new(image, region);
            using Mat gray = new();
            using Mat mask = new();
            Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(gray, mask, 180, 255, ThresholdTypes.Binary);
            Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            return
            [
                .. contours
                    .Select(Cv2.BoundingRect)
                    .Where(r => r.Width >= 20 && r.Height >= 20)
                    .Select(r => new Detection(new BoundingBox(r.X + region.X, r.Y + region.Y, r.Width, r.Height), VehicleClass.Car, 0.9f)),
            ];
        }

        public void Dispose()
        {
        }
    }
}
