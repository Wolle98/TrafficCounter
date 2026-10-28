using TrafficCounter.App.Components;

namespace TrafficCounter.App.Tests;

public class DateRangeTests
{
    [Fact]
    public void Last_week_is_the_previous_monday_to_sunday_and_last_14_days_end_today()
    {
        var today = new DateTime(2026, 10, 28); // a Wednesday
        Dictionary<string, (DateTime From, DateTime To)> ranges = DateRange.QuickRangesFor(today).ToDictionary(r => r.Label, r => (r.From, r.To));

        Assert.Equal((new DateTime(2026, 10, 19), new DateTime(2026, 10, 25)), ranges["Range.LastWeek"]);
        Assert.Equal((new DateTime(2026, 10, 15), today), ranges["Range.Last14"]);
        Assert.Equal((new DateTime(2026, 10, 26), today), ranges["Range.ThisWeek"]);
    }

    [Fact]
    public void On_a_monday_last_week_ends_yesterday()
    {
        var monday = new DateTime(2026, 10, 26);
        (string _, DateTime from, DateTime to) = DateRange.QuickRangesFor(monday).Single(r => r.Label == "Range.LastWeek");

        Assert.Equal(new DateTime(2026, 10, 19), from);
        Assert.Equal(new DateTime(2026, 10, 25), to);
    }
}
