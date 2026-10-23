using TrafficCounter.Core.Operations;

namespace TrafficCounter.Pipeline.Tests;

public class QualityMonitorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset Midnight = new(2026, 9, 28, 0, 30, 0, TimeSpan.FromHours(2));

    private static QualitySample Sample(float contrast, float sharpness, float? lineContrast = null, float? lineSharpness = null) =>
        new(100, contrast, sharpness, lineContrast ?? contrast, lineSharpness ?? sharpness);

    private static QualityMonitor Warmed(DateTimeOffset time, float contrast = 50, float sharpness = 400)
    {
        var monitor = new QualityMonitor();
        for (int i = 0; i < 30; i++)
        {
            monitor.Add(Sample(contrast, sharpness), time);
        }

        return monitor;
    }

    [Fact]
    public void Level_is_unknown_until_enough_samples_were_seen()
    {
        var monitor = new QualityMonitor();
        for (int i = 0; i < 10; i++)
        {
            monitor.Add(Sample(50, 400), Noon);
        }

        Assert.Equal(QualityLevel.Unknown, monitor.Current);
    }

    [Fact]
    public void Normal_images_are_good() => Assert.Equal(QualityLevel.Good, Warmed(Noon).Current);

    [Theory]
    [InlineData(50, 150, QualityLevel.Degraded)]  // drops on the window: sharpness at 37 %
    [InlineData(8, 400, QualityLevel.Unusable)]   // fogged window: contrast at 16 %
    [InlineData(45, 350, QualityLevel.Good)]
    public void Levels_follow_the_ratio_to_the_normal_value(float contrast, float sharpness, QualityLevel expected)
    {
        QualityMonitor monitor = Warmed(Noon);
        for (int i = 0; i < 3; i++)
        {
            monitor.Add(Sample(contrast, sharpness), Noon);
        }

        Assert.Equal(expected, monitor.Current);
    }

    [Fact]
    public void Night_is_judged_against_night_values()
    {
        // Low contrast and sharpness are normal at night and must not count as degraded.
        QualityMonitor monitor = Warmed(Noon);
        for (int i = 0; i < 30; i++)
        {
            monitor.Add(Sample(8, 40), Midnight);
        }

        Assert.NotEqual(QualityLevel.Degraded, monitor.Current);
        Assert.NotEqual(QualityLevel.Unusable, monitor.Current);
    }

    [Fact]
    public void Blur_only_at_the_counting_line_is_detected()
    {
        QualityMonitor monitor = Warmed(Noon);
        for (int i = 0; i < 3; i++)
        {
            monitor.Add(Sample(50, 400, lineContrast: 45, lineSharpness: 60), Noon); // a drop right on the line
        }

        Assert.Equal(QualityLevel.Unusable, monitor.Current);
    }

    [Fact]
    public void A_single_bad_sample_does_not_change_the_level()
    {
        QualityMonitor monitor = Warmed(Noon);
        bool changed = monitor.Add(Sample(5, 20), Noon); // e.g. a truck filling the band

        Assert.False(changed);
        Assert.Equal(QualityLevel.Good, monitor.Current);
    }

    [Fact]
    public void Bad_images_are_not_learned_as_normal()
    {
        QualityMonitor monitor = Warmed(Noon);
        for (int i = 0; i < 500; i++)
        {
            monitor.Add(Sample(50, 150), Noon); // dirty window for a long time
        }

        Assert.Equal(QualityLevel.Degraded, monitor.Current);
    }

    [Fact]
    public void Seeded_history_allows_judging_right_after_a_restart()
    {
        var monitor = new QualityMonitor();
        monitor.Seed([new QualityHistory(12, 50, 400, 600)]);
        for (int i = 0; i < 3; i++)
        {
            monitor.Add(Sample(50, 100), Noon);
        }

        Assert.Equal(QualityLevel.Degraded, monitor.Current);
    }

    [Fact]
    public void A_new_hour_is_judged_against_the_previous_hour_while_it_warms_up()
    {
        QualityMonitor monitor = Warmed(Noon);
        DateTimeOffset nextHour = Noon.AddHours(1);
        for (int i = 0; i < 5; i++)
        {
            Assert.False(monitor.Add(Sample(50, 400), nextHour));
        }

        Assert.Equal(QualityLevel.Good, monitor.Current);
        for (int i = 0; i < 3; i++)
        {
            monitor.Add(Sample(50, 100), nextHour);
        }

        Assert.Equal(QualityLevel.Degraded, monitor.Current);
    }

    [Fact]
    public void A_level_is_reported_only_after_it_held_for_the_hold_time()
    {
        var settled = new SettledQualityLevel(TimeSpan.FromMinutes(3));
        Assert.False(settled.Update(QualityLevel.Good, Noon));
        Assert.False(settled.Update(QualityLevel.Good, Noon.AddMinutes(2)));
        Assert.True(settled.Update(QualityLevel.Good, Noon.AddMinutes(3)));
        Assert.Equal(QualityLevel.Good, settled.Reported);
    }

    [Fact]
    public void A_brief_change_that_returns_is_not_reported()
    {
        var settled = new SettledQualityLevel(TimeSpan.FromMinutes(3));
        settled.Update(QualityLevel.Good, Noon);
        settled.Update(QualityLevel.Good, Noon.AddMinutes(3));

        Assert.False(settled.Update(QualityLevel.Unusable, Noon.AddMinutes(4)));
        Assert.False(settled.Update(QualityLevel.Good, Noon.AddMinutes(4.5))); // back within seconds
        Assert.False(settled.Update(QualityLevel.Unusable, Noon.AddMinutes(6)));
        Assert.False(settled.Update(QualityLevel.Unusable, Noon.AddMinutes(8)));
        Assert.Equal(QualityLevel.Good, settled.Reported);
    }

    [Fact]
    public void Flipping_between_bad_levels_is_reported_once_with_the_current_level()
    {
        var settled = new SettledQualityLevel(TimeSpan.FromMinutes(3));
        settled.Update(QualityLevel.Good, Noon);
        settled.Update(QualityLevel.Good, Noon.AddMinutes(3));

        Assert.False(settled.Update(QualityLevel.Unusable, Noon.AddMinutes(4)));
        Assert.False(settled.Update(QualityLevel.Degraded, Noon.AddMinutes(5)));
        Assert.True(settled.Update(QualityLevel.Unusable, Noon.AddMinutes(7)));
        Assert.Equal(QualityLevel.Unusable, settled.Reported);
    }
}
