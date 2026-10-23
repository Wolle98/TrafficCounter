using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Counting.Tests;

public class SpeedEstimationTests
{
    // Vertical counting line at x = 500 from y = 100 (position 0) to y = 400 (position 1).
    private static readonly LineSegment Line = new(new Point2(500, 100), new Point2(500, 400));
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(2));
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(1000.0 / 15);

    // 0.02 m per pixel at the far kerb (position 0.2), 0.01 m per pixel at the near kerb (position 0.8).
    private static readonly SpeedSettings Speed = new(0.02f, 0.01f, 0.2f, 0.8f, ZonePixels: 300);

    private static VehiclePassageEvent DriveAt(float pixelsPerSecond, float bottomY, float jitter = 0)
    {
        var engine = new CountingEngine(new CountingSettings(Line, EmitDistancePixels: 10_000, EmitDelaySeconds: 0, Speed: Speed));
        List<VehiclePassageEvent> events = [];
        var random = new Random(1);
        int frame = 0;
        for (float x = 1000; x >= 0; x -= pixelsPerSecond / 15)
        {
            float noisy = x + (jitter * (float)((random.NextDouble() * 2) - 1));
            var box = new BoundingBox(noisy - 40, bottomY - 50, 80, 50);
            events.AddRange(engine.Process(new TrackingResult(T0 + (FrameInterval * frame++), [new TrackedObject(1, box, VehicleClass.Car, 0.9f)], [])));
        }

        events.AddRange(engine.Process(new TrackingResult(T0 + (FrameInterval * frame), [], [1])));
        return Assert.Single(events);
    }

    [Fact]
    public void Speed_is_image_speed_times_the_scale_of_the_lane()
    {
        // Near kerb (bottom at y = 340, position 0.8): 0.01 m/px, 833 px/s → 8.33 m/s = 30 km/h.
        VehiclePassageEvent passage = DriveAt(833.3f, bottomY: 340);
        Assert.NotNull(passage.SpeedKmh);
        Assert.InRange(passage.SpeedKmh!.Value, 29.5f, 30.5f);
    }

    [Fact]
    public void Scale_is_interpolated_between_the_kerbs_in_pixels_per_meter()
    {
        // Middle of the road (position 0.5): 50 and 100 px/m at the kerbs give 75 px/m (perspective: the reciprocal
        // of the distance is linear in the image row), so 625 px/s → 8.33 m/s = 30 km/h.
        VehiclePassageEvent passage = DriveAt(625f, bottomY: 250);
        Assert.InRange(passage.SpeedKmh!.Value, 29.5f, 30.5f);
    }

    [Fact]
    public void Jittery_boxes_average_out()
    {
        VehiclePassageEvent passage = DriveAt(833.3f, bottomY: 340, jitter: 15);
        Assert.InRange(passage.SpeedKmh!.Value, 28.5f, 31.5f);
    }

    [Fact]
    public void Without_speed_settings_no_speed_is_given()
    {
        var engine = new CountingEngine(new CountingSettings(Line, EmitDistancePixels: 10_000, EmitDelaySeconds: 0));
        List<VehiclePassageEvent> events = [];
        int frame = 0;
        for (float x = 700; x >= 300; x -= 20)
        {
            events.AddRange(engine.Process(new TrackingResult(
                T0 + (FrameInterval * frame++), [new TrackedObject(1, new BoundingBox(x - 40, 290, 80, 50), VehicleClass.Car, 0.9f)], [])));
        }

        events.AddRange(engine.Process(new TrackingResult(T0 + (FrameInterval * frame), [], [1])));
        Assert.Null(Assert.Single(events).SpeedKmh);
    }

    [Fact]
    public void Too_few_positions_give_no_speed()
    {
        Assert.Null(Speed.Estimate([(0, 100, 0.5f), (0.1, 110, 0.5f)]));
    }
}
