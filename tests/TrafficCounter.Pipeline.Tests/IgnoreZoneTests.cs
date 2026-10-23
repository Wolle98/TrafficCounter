using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;
using TrafficCounter.Vision;

namespace TrafficCounter.Pipeline.Tests;

public class IgnoreZoneTests
{
    // Line from the far property line (0.44) to the near one (0.95); kerbs at 0.14 and 0.67 of it,
    // so the carriageway covers rows 0.51..0.78.
    private static SiteOptions Site(params RelativeRect[] ignoreZones) => new()
    {
        BandTop = 0.3f,
        BandBottom = 0.9f,
        LineStart = new RelativePoint { X = 0.5f, Y = 0.44f },
        LineEnd = new RelativePoint { X = 0.5f, Y = 0.95f },
        FarKerb = 0.14f,
        NearKerb = 0.67f,
        IgnoreZones = [.. ignoreZones],
    };

    private static readonly RelativeRect Driveway = new() { X = 0.55f, Y = 0.3f, Width = 0.2f, Height = 0.13f };

    [Fact]
    public void Carriageway_rows_follow_from_the_kerbs()
    {
        (float top, float bottom) = Site().CarriagewayRows!.Value;
        Assert.Equal(0.5114f, top, 3);
        Assert.Equal(0.7817f, bottom, 3);
    }

    [Fact]
    public void Ground_point_inside_a_zone_is_ignored()
    {
        SiteOptions site = Site(Driveway);
        Assert.True(site.IsIgnored(0.6f, 0.42f));
        Assert.False(site.IsIgnored(0.6f, 0.47f)); // far sidewalk just below the driveway
        Assert.False(site.IsIgnored(0.3f, 0.42f));
    }

    [Fact]
    public void Zones_on_the_carriageway_are_rejected()
    {
        Site(Driveway).Validate();
        var onRoad = new RelativeRect { X = 0.1f, Y = 0.7f, Width = 0.1f, Height = 0.15f };
        Assert.Throws<InvalidOperationException>(() => Site(onRoad).Validate());
    }

    [Fact]
    public void Zones_are_translated_for_band_only_recordings()
    {
        RelativeRect zone = Assert.Single(Site(Driveway).ForCrop(0.3f, 0.9f).IgnoreZones);
        Assert.Equal(0f, zone.Y, 4);
        Assert.Equal(0.13f / 0.6f, zone.Height, 4);
    }

    [Fact]
    public void Pipeline_drops_detections_standing_in_a_zone()
    {
        var frame = new Size(1000, 1000);
        Detection parked = Box(600, 420); // ground point in the driveway
        Detection moving = Box(300, 600); // on the road
        var tracker = new RecordingTracker();
        var pipeline = new CountingPipeline(new FixedDetector(parked, moving), tracker, Site(Driveway));

        using var frameData = new Frame(new Mat(frame, MatType.CV_8UC3, Scalar.Black), DateTimeOffset.Now, 1);
        FrameResult result = pipeline.Process(frameData);

        Assert.Equal([moving], tracker.Seen);
        Assert.Equal([moving], result.Detections);
    }

    [Fact]
    public void Pipeline_detects_only_inside_the_narrowed_band()
    {
        SiteOptions site = Site(Driveway);
        site.BandLeft = 0.2f;
        site.BandRight = 0.8f;
        var detector = new FixedDetector();
        var pipeline = new CountingPipeline(detector, new RecordingTracker(), site);

        using var frameData = new Frame(new Mat(new Size(1000, 1000), MatType.CV_8UC3, Scalar.Black), DateTimeOffset.Now, 1);
        FrameResult result = pipeline.Process(frameData);

        Assert.Equal(new Rect(200, 300, 600, 600), result.Band);
        Assert.Equal(result.Band, detector.LastRegion);
    }

    private static Detection Box(float centerX, float bottom) =>
        new(new BoundingBox(centerX - 80, bottom - 60, 160, 60), VehicleClass.Car, 0.9f);

    private sealed class FixedDetector(params Detection[] detections) : IObjectDetector
    {
        public string ModelName => "fixed";

        public string ExecutionProvider => "none";

        public Rect? LastRegion { get; private set; }

        public IReadOnlyList<Detection> Detect(Mat image, Rect region)
        {
            LastRegion = region;
            return detections;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingTracker : ITracker
    {
        public List<Detection> Seen { get; } = [];

        public TrackingResult Update(
            IReadOnlyList<Detection> detections, DateTimeOffset timestamp, Core.Operations.LightingCondition lighting)
        {
            Seen.AddRange(detections);
            return new TrackingResult(timestamp, [], []);
        }
    }
}

public class ProcessingOptionsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(2));

    [Theory]
    [InlineData(30, 0, 300)]
    [InlineData(30, 15, 150)]
    [InlineData(30, 10, 100)]
    [InlineData(20, 15, 150)]   // camera slowed down at night: still 15, not every second frame (10)
    [InlineData(20, 10, 100)]
    [InlineData(10, 15, 100)]   // fewer than MaxFps: every frame
    public void Max_fps_limits_the_processed_frames_on_average(double cameraFps, double maxFps, int expected)
    {
        using var pipeline = new CountingPipeline(new NoDetector(), new Tracking.ByteTracker(), new SiteOptions(), new ProcessingOptions { MaxFps = maxFps });
        int processed = 0;
        var jitter = new Random(1);
        for (int i = 0; i < cameraFps * 10; i++)
        {
            DateTimeOffset t = T0.AddSeconds((i / cameraFps) + (jitter.NextDouble() * 0.004));
            if (pipeline.ShouldProcess(t))
            {
                using var frame = new Frame(new Mat(100, 200, MatType.CV_8UC3, Scalar.Black), t, i);
                pipeline.Process(frame);
                processed++;
            }
        }

        Assert.InRange(processed, expected - 2, expected + 2);
    }

    [Fact]
    public void Motion_gate_skips_detection_on_a_static_scene()
    {
        var detector = new NoDetector();
        var processing = new ProcessingOptions();
        processing.MotionGate.Enabled = true;
        using var pipeline = new CountingPipeline(detector, new Tracking.ByteTracker(), new SiteOptions(), processing);
        for (int i = 0; i < 30; i++)
        {
            using var frame = new Frame(new Mat(100, 200, MatType.CV_8UC3, Scalar.Gray), T0.AddSeconds(i / 30.0), i);
            pipeline.Process(frame);
        }

        Assert.Equal(1, detector.Calls);
        Assert.Equal((1L, 29L), pipeline.GateStatistics);
    }

    private sealed class NoDetector : IObjectDetector
    {
        public int Calls { get; private set; }

        public string ModelName => "none";

        public string ExecutionProvider => "none";

        public IReadOnlyList<Detection> Detect(Mat image, Rect region)
        {
            Calls++;
            return [];
        }

        public void Dispose()
        {
        }
    }
}

