using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Counting.Tests;

/// <summary>Speeds are discarded when the source did not deliver frames at its normal rate (ADR-0063).</summary>
public class StalledStreamTests
{
    // Vertical counting line at x = 500; 0.01 m per pixel everywhere.
    private static readonly LineSegment Line = new(new Point2(500, 100), new Point2(500, 400));
    private static readonly DateTimeOffset T0 = new(2026, 11, 8, 10, 30, 0, TimeSpan.FromHours(1));
    private static readonly SpeedSettings Speed = new(0.01f, 0.01f, 0.2f, 0.8f, ZonePixels: 300);

    /// <summary>
    /// A minute of a 25 fps source without traffic, then a car at 30 km/h (833 px/s). Every second captured frame is
    /// processed. While the car is near the line, frames arrive at <paramref name="arrivalsPerSecond"/>, each showing
    /// the next 1/25 s of the scene: a stalled stream that falls behind (slower) or catches up (faster).
    /// </summary>
    private static VehiclePassageEvent Drive(double arrivalsPerSecond, bool withSequence = true)
    {
        var engine = new CountingEngine(new CountingSettings(Line, EmitDistancePixels: 10_000, EmitDelaySeconds: 0, Speed: Speed));
        long sequence = 0;
        double arrival = 0;
        for (; arrival < 60; arrival += 1 / 25.0)
        {
            engine.NoteCapture(T0 + TimeSpan.FromSeconds(arrival), ++sequence);
        }

        List<VehiclePassageEvent> events = [];
        double sceneTime = 0;
        for (float x = 1000; x >= 0; sceneTime += 1 / 25.0)
        {
            x = 1000 - (833.3f * (float)sceneTime);
            bool nearLine = Math.Abs(x - 500) < 400;
            arrival += nearLine ? 1 / arrivalsPerSecond : 1 / 25.0;
            sequence++;
            if (sequence % 2 == 0)
            {
                var box = new BoundingBox(x - 40, 290, 80, 50);
                events.AddRange(engine.Process(
                    new TrackingResult(T0 + TimeSpan.FromSeconds(arrival), [new TrackedObject(1, box, VehicleClass.Car, 0.9f)], []),
                    withSequence ? sequence : null));
            }
        }

        events.AddRange(engine.Process(new TrackingResult(T0 + TimeSpan.FromSeconds(arrival + 0.1), [], [1]), withSequence ? sequence + 1 : null));
        return Assert.Single(events);
    }

    [Fact]
    public void A_normal_stream_keeps_the_speed()
    {
        VehiclePassageEvent passage = Drive(25);

        Assert.InRange(passage.SpeedKmh!.Value, 29.5f, 30.5f);
        Assert.InRange(passage.Track!.CaptureRateRatio!.Value, 0.97f, 1.03f);
        Assert.False(passage.Track.SpeedDiscarded);
    }

    [Theory]
    [InlineData(18)] // falls behind: the speed would come out at 72 %
    [InlineData(35)] // catches up: it would come out at 140 %
    public void A_stalled_stream_gives_no_speed(double arrivalsPerSecond)
    {
        VehiclePassageEvent passage = Drive(arrivalsPerSecond);

        Assert.Null(passage.SpeedKmh);
        Assert.True(passage.Track!.SpeedDiscarded);
        Assert.InRange(passage.Track.CaptureRateRatio!.Value, (arrivalsPerSecond / 25) - 0.05, (arrivalsPerSecond / 25) + 0.05);
        Assert.NotNull(passage.Track.MeanSpeedKmh); // still tells a vehicle from a pedestrian
    }

    [Fact]
    public void A_small_deviation_keeps_the_speed()
    {
        VehiclePassageEvent passage = Drive(23);

        Assert.NotNull(passage.SpeedKmh);
        Assert.False(passage.Track!.SpeedDiscarded);
    }

    [Fact]
    public void Without_capture_sequence_numbers_nothing_is_checked()
    {
        VehiclePassageEvent passage = Drive(18, withSequence: false);

        Assert.NotNull(passage.SpeedKmh);
        Assert.Null(passage.Track!.CaptureRateRatio);
    }

    [Fact]
    public void The_normal_rate_ignores_a_stall_of_half_a_minute()
    {
        var rate = new CaptureRate();
        long sequence = 0;
        double at = 0;
        void Run(double seconds, double fps)
        {
            for (double end = at + seconds; at < end; at += 1 / fps)
            {
                rate.Add(T0 + TimeSpan.FromSeconds(at), ++sequence);
            }
        }

        Run(120, 25);
        Run(30, 18);
        Run(5, 31);

        Assert.InRange(rate.PerSecond!.Value, 24.5, 25.5);
    }

    [Fact]
    public void The_normal_rate_starts_over_for_a_new_source()
    {
        var rate = new CaptureRate();
        for (int i = 0; i < 25 * 60; i++)
        {
            rate.Add(T0 + TimeSpan.FromSeconds(i / 25.0), i + 1);
        }

        Assert.NotNull(rate.PerSecond);

        rate.Add(T0 + TimeSpan.FromSeconds(61), 1);

        Assert.Null(rate.PerSecond);
    }
}
