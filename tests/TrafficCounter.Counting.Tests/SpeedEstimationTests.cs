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
    public void Waiting_before_the_line_does_not_lower_the_speed()
    {
        // Waits 6 s in the speed zone (e.g. for oncoming traffic at a narrowing), then drives on at 30 km/h.
        var engine = new CountingEngine(new CountingSettings(Line, EmitDistancePixels: 10_000, EmitDelaySeconds: 0, Speed: Speed));
        List<VehiclePassageEvent> events = [];
        int frame = 0;
        void Step(float x) => events.AddRange(engine.Process(new TrackingResult(
            T0 + (FrameInterval * frame++), [new TrackedObject(1, new BoundingBox(x - 40, 290, 80, 50), VehicleClass.Car, 0.9f)], [])));
        for (int i = 0; i < 90; i++)
        {
            Step(700);
        }

        for (float x = 700; x >= 0; x -= 833.3f / 15)
        {
            Step(x);
        }

        events.AddRange(engine.Process(new TrackingResult(T0 + (FrameInterval * frame), [], [1])));
        Assert.InRange(Assert.Single(events).SpeedKmh!.Value, 29f, 31f);
    }

    [Fact]
    public void A_vehicle_stopping_behind_the_line_gets_no_speed()
    {
        // Crosses at 30 km/h and parks 100 px behind the line, still in the zone; the track ends there.
        var engine = new CountingEngine(new CountingSettings(Line, EmitDistancePixels: 10_000, EmitDelaySeconds: 0, Speed: Speed));
        List<VehiclePassageEvent> events = [];
        int frame = 0;
        void Step(float x) => events.AddRange(engine.Process(new TrackingResult(
            T0 + (FrameInterval * frame++), [new TrackedObject(1, new BoundingBox(x - 40, 290, 80, 50), VehicleClass.Car, 0.9f)], [])));
        for (float x = 900; x >= 400; x -= 833.3f / 15)
        {
            Step(x);
        }

        for (int i = 0; i < 25; i++)
        {
            Step(400 + (i % 2));
        }

        events.AddRange(engine.Process(new TrackingResult(T0 + (FrameInterval * frame), [], [1])));
        VehiclePassageEvent passage = Assert.Single(events);
        Assert.Null(passage.SpeedKmh);
        Assert.NotNull(passage.Track?.MeanSpeedKmh); // still there for telling pedestrians from vehicles
    }

    [Fact]
    public void Pausing_behind_the_line_keeps_the_positions_up_to_the_line()
    {
        // Walks across the line at 1.2 m/s, stands 3 s behind it at the edge of the zone and walks out of it: the
        // mean speed of the way to the line must still be there.
        var engine = new CountingEngine(new CountingSettings(Line, EmitDistancePixels: 10_000, EmitDelaySeconds: 0, Speed: Speed));
        List<VehiclePassageEvent> events = [];
        int frame = 0;
        float x = 600;
        void Step() => events.AddRange(engine.Process(new TrackingResult(
            T0 + (FrameInterval * frame++), [new TrackedObject(1, new BoundingBox(x - 40, 290, 80, 50), VehicleClass.EScooter, 0.9f)], [])));
        for (; x >= 215; x -= 120f / 15)
        {
            Step();
        }

        for (int i = 0; i < 45; i++)
        {
            Step();
        }

        for (int i = 0; i < 20; i++, x -= 120f / 15)
        {
            Step();
        }

        events.AddRange(engine.Process(new TrackingResult(T0 + (FrameInterval * frame), [], [1])));
        Assert.InRange(Assert.Single(events).Track!.MeanSpeedKmh!.Value, 0.5f, 5f);
    }

    [Fact]
    public void The_mean_speed_includes_the_stop()
    {
        // Walks 1.2 m/s for 1.5 s, stands 1.5 s: no driving speed, mean 0.6 m/s ≈ 2.2 km/h.
        List<(double, float, float)> samples = [.. Enumerable.Range(0, 45).Select(i => (i / 15.0, 700 - (Math.Min(i, 22) * 1.2f / 0.01f / 15), 0.8f))];
        Assert.Null(Speed.Estimate(samples));
        Assert.InRange(Speed.MeanSpeed(samples)!.Value, 1.5f, 3.5f);
    }

    [Theory]
    [InlineData(1.2f, true)]
    [InlineData(0.5f, false)]
    public void A_stretch_slower_than_the_stop_speed_means_a_stop(float metersPerSecond, bool measured)
    {
        // 0.01 m per pixel on the near kerb: steady driving at the given speed for 3 s.
        List<(double, float, float)> samples = [.. Enumerable.Range(0, 45).Select(i => (i / 15.0, 700 - (i * metersPerSecond / 0.01f / 15), 0.8f))];
        Assert.Equal(measured, Speed.Estimate(samples) is not null);
    }

    [Fact]
    public void Too_few_positions_give_no_speed()
    {
        Assert.Null(Speed.Estimate([(0, 100, 0.5f), (0.1, 110, 0.5f)]));
    }
}
