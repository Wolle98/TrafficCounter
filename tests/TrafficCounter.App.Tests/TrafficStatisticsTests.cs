using TrafficCounter.App.Data;
using TrafficCounter.Core;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.App.Tests;

public class TrafficStatisticsTests
{
    private static readonly DateTime Day = new(2026, 10, 5); // a Monday

    private static PassageRow Car(DateTime at, float? speed = null, TravelDirection direction = TravelDirection.A) =>
        new(at, direction, VehicleClass.Car, speed);

    private static HashSet<DateTime> Covered(DateTime from, int minutes) =>
        TrafficStatistics.CoveredMinutes(Enumerable.Range(0, minutes)
            .Select(m => new MinuteRow(from.AddMinutes(m), SourceState.Ok, QualityLevel.Good, LightingCondition.Day)));

    [Fact]
    public void Rate_per_hour_refers_to_the_measured_minutes()
    {
        // 30 vehicles in the first half hour, then the counter was off.
        List<PassageRow> passages = [.. Enumerable.Range(0, 30).Select(i => Car(Day.AddHours(8).AddMinutes(i))) ];
        List<Bucket> buckets = TrafficStatistics.Buckets(passages, Covered(Day.AddHours(8), 30), Day.AddHours(8), Day.AddHours(9), TimeSpan.FromHours(1));

        Bucket hour = Assert.Single(buckets);
        Assert.Equal(30, hour.Count);
        Assert.Equal(0.5, hour.Coverage, 3);
        Assert.Equal(60, hour.RatePerHour!.Value, 3);
    }

    [Fact]
    public void Vehicles_of_minutes_not_measured_do_not_raise_the_rate()
    {
        // 30 measured minutes with one car each, then 30 minutes of a degraded stream that still counted 20 cars.
        List<PassageRow> passages =
        [
            .. Enumerable.Range(0, 30).Select(i => Car(Day.AddHours(8).AddMinutes(i))),
            .. Enumerable.Range(0, 20).Select(i => Car(Day.AddHours(8).AddMinutes(30 + i))),
        ];
        Bucket hour = Assert.Single(TrafficStatistics.Buckets(passages, Covered(Day.AddHours(8), 30), Day.AddHours(8), Day.AddHours(9), TimeSpan.FromHours(1)));

        Assert.Equal(50, hour.Count); // all counted vehicles are still shown
        Assert.Equal(60, hour.RatePerHour!.Value, 3); // the rate only from the measured half hour
    }

    [Fact]
    public void Day_pattern_has_one_row_per_date_of_the_weekday_newest_first()
    {
        // Day is a Monday; three Mondays in the range, measured 8-9 h on two of them.
        DateTime first = Day, second = Day.AddDays(7), third = Day.AddDays(14);
        HashSet<DateTime> covered = [.. Covered(first.AddHours(8), 60), .. Covered(third.AddHours(8), 30)];
        List<PassageRow> passages =
        [
            .. Enumerable.Range(0, 12).Select(i => Car(first.AddHours(8).AddMinutes(5 * i))),
            .. Enumerable.Range(0, 5).Select(i => Car(third.AddHours(8).AddMinutes(5 * i))),
            Car(first.AddDays(1).AddHours(8)), // a Tuesday: not part of the pattern
        ];

        (IReadOnlyList<DateTime> dates, double?[,] values) =
            TrafficStatistics.DayHeatmap(passages, covered, DayOfWeek.Monday, Day, third.AddDays(1));

        Assert.Equal([third, second, first], dates);
        Assert.Equal(10, values[0, 8]!.Value, 3); // 5 vehicles in the measured half hour
        Assert.Null(values[1, 8]);                // not measured
        Assert.Equal(12, values[2, 8]!.Value, 3);
        Assert.Null(values[2, 9]);
    }

    [Fact]
    public void Bucket_without_measurement_has_no_rate()
    {
        List<Bucket> buckets = TrafficStatistics.Buckets([], [], Day, Day.AddHours(2), TimeSpan.FromHours(1));
        Assert.All(buckets, b => Assert.Null(b.RatePerHour));
        Assert.Equal(2, buckets.Count);
    }

    [Fact]
    public void Current_bucket_counts_only_the_minutes_that_have_passed()
    {
        List<Bucket> buckets = TrafficStatistics.Buckets([], Covered(Day.AddHours(10), 20), Day.AddHours(10), Day.AddHours(12), TimeSpan.FromHours(1), now: Day.AddHours(10).AddMinutes(20));
        Assert.Equal(20, buckets[0].Minutes);
        Assert.Equal(1, buckets[0].Coverage, 3);
        Assert.Equal(0, buckets[1].Minutes);
    }

    [Fact]
    public void Typical_day_averages_the_same_weekday_only()
    {
        // Two Mondays with 10 and 20 vehicles between 7 and 8, a Tuesday with 100.
        List<PassageRow> passages =
        [
            .. Enumerable.Range(0, 10).Select(i => Car(Day.AddDays(-7).AddHours(7).AddMinutes(i))),
            .. Enumerable.Range(0, 20).Select(i => Car(Day.AddDays(-14).AddHours(7).AddMinutes(i))),
            .. Enumerable.Range(0, 100).Select(i => Car(Day.AddDays(-6).AddHours(7).AddSeconds(i * 30))),
        ];
        HashSet<DateTime> covered = [.. Covered(Day.AddDays(-7).AddHours(7), 60), .. Covered(Day.AddDays(-14).AddHours(7), 60), .. Covered(Day.AddDays(-6).AddHours(7), 60)];

        double?[] monday = TrafficStatistics.TypicalDay(passages, covered, DayOfWeek.Monday);
        Assert.Equal(15, monday[7]!.Value, 3);
        Assert.Null(monday[8]);
    }

    [Fact]
    public void Week_heatmap_starts_on_monday()
    {
        double?[,] map = TrafficStatistics.WeekHeatmap([Car(Day.AddHours(9).AddMinutes(5))], Covered(Day.AddHours(9), 60));
        Assert.Equal(1, map[0, 9]!.Value, 3);
        Assert.Null(map[1, 9]);
    }

    [Fact]
    public void Speed_summary_ignores_bicycles_and_gives_v85()
    {
        List<PassageRow> passages =
        [
            .. Enumerable.Range(1, 100).Select(i => Car(Day.AddMinutes(i), speed: i)),
            new PassageRow(Day, TravelDirection.A, VehicleClass.Bicycle, 200),
        ];

        SpeedSummary summary = TrafficStatistics.Speeds(passages)!;
        Assert.Equal(100, summary.Count);
        Assert.Equal(50.5, summary.Median, 3);
        Assert.Equal(85.15, summary.V85, 2);
        Assert.Equal(0.70, summary.ShareOver30, 3);
    }
}
