using OpenCvSharp;

namespace TrafficCounter.Vision.Tests;

public class MotionGateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(2));

    private static MotionGateOptions Options() => new() { Enabled = true, HoldSeconds = 1, MaxSkipSeconds = 5 };

    /// <summary>Grey street with an optional bright "vehicle" rectangle at <paramref name="x"/>.</summary>
    private static Mat Band(int? x = null)
    {
        var band = new Mat(300, 1200, MatType.CV_8UC3, new Scalar(90, 90, 90));
        if (x is int left)
        {
            Cv2.Rectangle(band, new Rect(left, 100, 200, 120), new Scalar(220, 220, 220), -1);
        }

        return band;
    }

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    [Fact]
    public void Static_scene_skips_the_detector_after_the_first_frame()
    {
        using var gate = new MotionGate(Options());
        using Mat empty = Band();
        Assert.True(gate.ShouldDetect(empty, At(0), []));
        Assert.False(gate.ShouldDetect(empty, At(0.1), []));
        Assert.False(gate.ShouldDetect(empty, At(2), []));
    }

    [Fact]
    public void Moving_vehicle_runs_the_detector_and_keeps_it_running_for_the_hold_time()
    {
        using var gate = new MotionGate(Options());
        using Mat empty = Band();
        using Mat at100 = Band(100);
        using Mat at140 = Band(140);
        gate.ShouldDetect(empty, At(0), []);
        gate.ShouldDetect(empty, At(0.1), []);

        Assert.True(gate.ShouldDetect(at100, At(0.2), []));
        Assert.True(gate.ShouldDetect(at140, At(0.3), []));
        Assert.True(gate.ShouldDetect(at140, At(0.8), []));   // stopped, but within the hold time
        Assert.False(gate.ShouldDetect(at140, At(1.6), []));  // parked
    }

    [Fact]
    public void Changes_inside_ignore_zones_do_not_count()
    {
        using var gate = new MotionGate(Options());
        using Mat empty = Band();
        using Mat parked = Band(100);
        Rect[] driveway = [new Rect(50, 50, 400, 250)];
        gate.ShouldDetect(empty, At(0), driveway);
        gate.ShouldDetect(empty, At(0.1), driveway);

        Assert.False(gate.ShouldDetect(parked, At(0.2), driveway));
    }

    [Fact]
    public void Detector_runs_at_least_every_max_skip_seconds()
    {
        using var gate = new MotionGate(Options());
        using Mat empty = Band();
        gate.ShouldDetect(empty, At(0), []);
        Assert.False(gate.ShouldDetect(empty, At(4.9), []));
        Assert.True(gate.ShouldDetect(empty, At(5.1), []));
    }

    [Fact]
    public void Disabled_gate_always_detects()
    {
        using var gate = new MotionGate(new MotionGateOptions());
        using Mat empty = Band();
        gate.ShouldDetect(empty, At(0), []);
        Assert.True(gate.ShouldDetect(empty, At(0.1), []));
    }
}
