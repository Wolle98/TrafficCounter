using TrafficCounter.Core.Operations;

namespace TrafficCounter.Core.Tests;

public class PaneMonitorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 21, 0, 0, TimeSpan.FromHours(2));

    /// <summary>One window of 120 s with a sample every 10 s, then judged.</summary>
    private static void Window(PaneMonitor monitor, DateTimeOffset start, int discs, LightingCondition lighting = LightingCondition.Night)
    {
        for (int s = 0; s < 120; s += 10)
        {
            monitor.AddSample(discs, start.AddSeconds(s));
        }

        monitor.Evaluate(start.AddSeconds(120), lighting);
    }

    [Fact]
    public void A_rainy_window_starts_an_episode_that_ends_after_five_clear_windows()
    {
        var monitor = new PaneMonitor(T0);
        DateTimeOffset t = T0;
        Window(monitor, t, 15);
        Assert.False(monitor.HasDrops);

        Window(monitor, t = t.AddSeconds(120), 70);
        Assert.True(monitor.HasDrops);
        Assert.Equal(70, monitor.LastMedian);

        for (int i = 0; i < 4; i++)
        {
            Window(monitor, t = t.AddSeconds(120), 15);
            Assert.True(monitor.HasDrops);
        }

        Window(monitor, t.AddSeconds(120), 15);
        Assert.False(monitor.HasDrops);
    }

    [Fact]
    public void Single_frames_with_many_lights_do_not_count()
    {
        // A car with its headlights in a few frames of the window: the median stays low.
        var monitor = new PaneMonitor(T0);
        for (int s = 0; s < 120; s += 10)
        {
            monitor.AddSample(s < 30 ? 60 : 15, T0.AddSeconds(s));
        }

        monitor.Evaluate(T0.AddSeconds(120), LightingCondition.Night);
        Assert.False(monitor.HasDrops);
    }

    [Fact]
    public void Daylight_ends_an_episode()
    {
        var monitor = new PaneMonitor(T0);
        Window(monitor, T0, 70);
        Assert.True(monitor.HasDrops);

        monitor.Evaluate(T0.AddSeconds(240), LightingCondition.Day);
        Assert.False(monitor.HasDrops);
    }

    [Fact]
    public void Samples_are_due_every_ten_seconds_and_never_when_disabled()
    {
        var monitor = new PaneMonitor(T0);
        Assert.True(monitor.IsSampleDue(T0));
        monitor.AddSample(10, T0);
        Assert.False(monitor.IsSampleDue(T0.AddSeconds(9)));
        Assert.True(monitor.IsSampleDue(T0.AddSeconds(10)));

        Assert.False(new PaneMonitor(T0, new PaneMonitorSettings { Enabled = false }).IsSampleDue(T0));
    }
}
