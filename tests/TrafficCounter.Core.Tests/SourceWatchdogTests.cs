using TrafficCounter.Core.Operations;

namespace TrafficCounter.Core.Tests;

public class SourceWatchdogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 11, 11, 30, 0, TimeSpan.FromHours(2));

    /// <summary>Feeds one window of 120 s: frames per second and one brightness sample every 5 s.</summary>
    private static bool Window(SourceWatchdog watchdog, DateTimeOffset start, double fps, double mean, double spread)
    {
        for (int second = 0; second < 120; second += 5)
        {
            watchdog.AddFrames((long)(fps * 5));
            watchdog.AddSample(mean, spread);
        }

        return watchdog.Evaluate(start.AddSeconds(120));
    }

    [Fact]
    public void Nothing_is_judged_before_the_window_ends()
    {
        var watchdog = new SourceWatchdog(T0);
        watchdog.AddSample(0, 0);

        Assert.False(watchdog.Evaluate(T0.AddSeconds(119)));
        Assert.False(watchdog.IsStalled);
    }

    [Fact]
    public void A_normal_picture_is_healthy_and_its_rate_is_learned()
    {
        var watchdog = new SourceWatchdog(T0);

        Assert.True(Window(watchdog, T0, fps: 23, mean: 140, spread: 35));
        Assert.False(watchdog.IsStalled);
        Assert.Equal(23, watchdog.NormalRate, 0.5);
    }

    [Fact]
    public void Black_frames_are_a_stall()
    {
        var watchdog = new SourceWatchdog(T0);
        Window(watchdog, T0, fps: 23, mean: 0, spread: 0);

        Assert.Equal(StallReason.Black, watchdog.Stall);
    }

    [Fact]
    public void A_dark_night_scene_is_no_stall()
    {
        // At night the image is dark (mean ~11), but sensor noise keeps a spread.
        var watchdog = new SourceWatchdog(T0);
        Window(watchdog, T0, fps: 23, mean: 11, spread: 8);

        Assert.False(watchdog.IsStalled);
    }

    [Fact]
    public void A_collapsed_frame_rate_is_a_stall_once_the_normal_rate_is_known()
    {
        var watchdog = new SourceWatchdog(T0);
        Window(watchdog, T0, fps: 23, mean: 140, spread: 35);
        Window(watchdog, T0.AddSeconds(120), fps: 1, mean: 140, spread: 35);

        Assert.Equal(StallReason.FrameRate, watchdog.Stall);
        Assert.Equal(23, watchdog.NormalRate, 0.5); // the stalled window is not learned
    }

    [Fact]
    public void A_lasting_stall_is_judged_again_every_window_and_lifted_by_a_healthy_one()
    {
        var watchdog = new SourceWatchdog(T0);
        Window(watchdog, T0, fps: 1, mean: 0, spread: 0);
        Assert.True(Window(watchdog, T0.AddSeconds(120), fps: 1, mean: 0, spread: 0));
        Assert.True(watchdog.IsStalled);

        Window(watchdog, T0.AddSeconds(240), fps: 23, mean: 140, spread: 35);

        Assert.False(watchdog.IsStalled);
    }

    [Fact]
    public void A_few_black_samples_among_normal_ones_are_no_stall()
    {
        var watchdog = new SourceWatchdog(T0);
        watchdog.AddFrames(2760);
        watchdog.AddSample(0, 0);
        watchdog.AddSample(140, 35);
        watchdog.AddSample(0, 0);
        watchdog.Evaluate(T0.AddSeconds(120));

        Assert.False(watchdog.IsStalled);
    }

    [Fact]
    public void An_ip_camera_slower_than_its_nominal_rate_is_degraded_until_it_is_stable_for_a_while()
    {
        // 25 fps nominal; for a while only about 21 fps arrive (as seen for two hours), then 25 again.
        var watchdog = new SourceWatchdog(T0, nominalRate: 25);
        DateTimeOffset t = T0;
        Window(watchdog, t, fps: 25, mean: 140, spread: 35);
        Assert.False(watchdog.IsDegraded);

        Window(watchdog, t = t.AddSeconds(120), fps: 21, mean: 140, spread: 35);
        Assert.True(watchdog.IsDegraded);
        Assert.False(watchdog.IsStalled); // still a usable picture, no reopening

        for (int i = 0; i < 4; i++)
        {
            Window(watchdog, t = t.AddSeconds(120), fps: 25, mean: 140, spread: 35);
            Assert.True(watchdog.IsDegraded);
        }

        Window(watchdog, t.AddSeconds(120), fps: 25, mean: 140, spread: 35);
        Assert.False(watchdog.IsDegraded);
    }

    [Fact]
    public void A_usb_camera_slowing_down_at_night_is_not_degraded()
    {
        // No nominal rate: 30 fps by day, 20 fps with the longer night exposure.
        var watchdog = new SourceWatchdog(T0);
        Window(watchdog, T0, fps: 30, mean: 140, spread: 35);
        Window(watchdog, T0.AddSeconds(120), fps: 20, mean: 20, spread: 8);

        Assert.False(watchdog.IsDegraded);
    }

    [Fact]
    public void An_interruption_starts_an_episode_that_further_interruptions_extend()
    {
        var watchdog = new SourceWatchdog(T0, nominalRate: 25);
        DateTimeOffset t = T0;
        watchdog.NoteInterruption();
        Assert.True(watchdog.IsDegraded);

        for (int i = 0; i < 4; i++)
        {
            Window(watchdog, t, fps: 25, mean: 140, spread: 35);
            t = t.AddSeconds(120);
        }

        watchdog.NoteInterruption(); // again before the episode ended: the count of healthy windows starts over
        for (int i = 0; i < 4; i++)
        {
            Window(watchdog, t, fps: 25, mean: 140, spread: 35);
            t = t.AddSeconds(120);
        }

        Assert.True(watchdog.IsDegraded);
        Window(watchdog, t, fps: 25, mean: 140, spread: 35);
        Assert.False(watchdog.IsDegraded);
    }
}
