using TrafficCounter.Core.Operations;

namespace TrafficCounter.Core.Tests;

public class MinuteStatisticsCollectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 18, 0, 20, TimeSpan.FromHours(2));

    private static MinuteStatisticsCollector Create() => new(cameraId: 1, operatingPeriodId: 7, T0, TimeZoneInfo.Utc);

    [Fact]
    public void Nothing_is_reported_before_the_minute_ends() =>
        Assert.Empty(Create().Tick(T0.AddSeconds(39)));

    [Fact]
    public void Counters_of_a_minute_are_reported_when_it_ends()
    {
        MinuteStatisticsCollector collector = Create();
        collector.SourceState = SourceState.Ok;
        collector.AddCaptured(3);
        collector.AddDropped(1);
        collector.AddProcessed(detections: 2);
        collector.AddProcessed(detections: 0);
        collector.AddPassages(1);
        collector.AddQualitySample(100, 40, 300);
        collector.AddQualitySample(120, 60, 500);

        MinuteStatistics row = Assert.Single(collector.Tick(T0.AddSeconds(45)));

        Assert.Equal(new DateTimeOffset(2026, 9, 28, 18, 0, 0, TimeSpan.FromHours(2)), row.MinuteStart);
        Assert.Equal((1, 7), (row.CameraId, row.OperatingPeriodId));
        Assert.Equal((3, 2, 1, 2, 1), (row.FramesCaptured, row.FramesProcessed, row.FramesDropped, row.Detections, row.Passages));
        Assert.Equal(SourceState.Ok, row.SourceState);
        Assert.Equal((110f, 50f, 400f), (row.Brightness!.Value, row.Contrast!.Value, row.Sharpness!.Value));
    }

    [Fact]
    public void Minutes_without_frames_are_reported_as_source_unavailable()
    {
        MinuteStatisticsCollector collector = Create();
        collector.SourceState = SourceState.Ok;
        collector.AddCaptured();
        collector.SourceState = SourceState.Unavailable; // camera unplugged

        IReadOnlyList<MinuteStatistics> rows = collector.Tick(T0.AddMinutes(3));

        Assert.Equal(3, rows.Count);
        Assert.Equal(SourceState.Ok, rows[0].SourceState); // was up during the first minute
        Assert.All(rows.Skip(1), r =>
        {
            Assert.Equal(SourceState.Unavailable, r.SourceState);
            Assert.Equal(0, r.FramesCaptured);
            Assert.Null(r.Brightness);
        });
        Assert.Equal(rows[0].MinuteStart.AddMinutes(2), rows[2].MinuteStart);
    }

    [Fact]
    public void Frames_of_a_stalled_source_do_not_count_as_measured()
    {
        MinuteStatisticsCollector collector = Create();
        collector.SourceState = SourceState.Ok;
        collector.AddCaptured(900);
        collector.SourceState = SourceState.Unavailable; // watchdog: only black frames
        collector.Tick(T0.AddSeconds(45));
        collector.AddCaptured(60);

        MinuteStatistics row = Assert.Single(collector.Tick(T0.AddSeconds(105)));

        Assert.Equal(SourceState.Unavailable, row.SourceState);
        Assert.Equal(60, row.FramesCaptured);
    }

    [Fact]
    public void Source_never_seen_is_unavailable() =>
        Assert.Equal(SourceState.Unavailable, Assert.Single(Create().Tick(T0.AddSeconds(40))).SourceState);

    [Fact]
    public void Worst_quality_of_a_minute_is_kept_and_reset_afterwards()
    {
        MinuteStatisticsCollector collector = Create();
        collector.Quality = QualityLevel.Good;
        collector.Quality = QualityLevel.Unusable;
        collector.Quality = QualityLevel.Degraded;

        Assert.Equal(QualityLevel.Unusable, Assert.Single(collector.Tick(T0.AddSeconds(40))).Quality);
        Assert.Equal(QualityLevel.Unknown, Assert.Single(collector.Tick(T0.AddSeconds(100))).Quality);
    }

    [Fact]
    public void Flush_reports_the_partial_minute() =>
        Assert.Equal(1, Create().Also(c => c.AddPassages(1)).Flush().Passages);

    [Fact]
    public void Minute_start_follows_the_configured_time_zone()
    {
        TimeZoneInfo berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

        // 2026-10-25 02:59 CEST (+02:00) -> clocks go back; the next minute is 02:00 CET (+01:00).
        var start = new DateTimeOffset(2026, 10, 25, 2, 59, 30, TimeSpan.FromHours(2));
        var collector = new MinuteStatisticsCollector(1, 1, start, berlin);

        IReadOnlyList<MinuteStatistics> rows = collector.Tick(start.AddMinutes(2));

        Assert.Equal(TimeSpan.FromHours(2), rows[0].MinuteStart.Offset);
        Assert.Equal(TimeSpan.FromHours(1), rows[1].MinuteStart.Offset);
        Assert.Equal(TimeSpan.FromMinutes(1), rows[1].MinuteStart - rows[0].MinuteStart);
    }

    [Fact]
    public void A_minute_degraded_at_any_time_is_not_fully_measured()
    {
        var collector = Create();
        collector.SourceState = SourceState.Ok;
        collector.AddCaptured(1200);
        collector.Degraded = true;
        collector.Degraded = false; // recovered within the minute: the minute is still affected
        Assert.Equal(SourceState.Degraded, Assert.Single(collector.Tick(T0.AddSeconds(40))).SourceState);

        collector.AddCaptured(1500);
        Assert.Equal(SourceState.Ok, Assert.Single(collector.Tick(T0.AddSeconds(100))).SourceState);
    }

    [Fact]
    public void A_degraded_source_without_frames_stays_unavailable()
    {
        var collector = Create();
        collector.Degraded = true;
        collector.SourceState = SourceState.Unavailable;

        Assert.Equal(SourceState.Unavailable, Assert.Single(collector.Tick(T0.AddSeconds(40))).SourceState);
    }
}

internal static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
