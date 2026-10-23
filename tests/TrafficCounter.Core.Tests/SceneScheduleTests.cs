using TrafficCounter.Core.Operations;

namespace TrafficCounter.Core.Tests;

public class SceneScheduleTests
{
    // Centre of Germany; the sun times are compared with published tables to a few minutes.
    private const double Latitude = 51.0;
    private const double Longitude = 10.0;

    [Theory]
    [InlineData("2026-10-23T12:00:00+02:00", "07:55", "18:12", "08:30", "18:00")] // summer time, late October
    [InlineData("2026-10-26T12:00:00+01:00", "07:00", "17:06", "07:30", "17:00")] // first day of winter time
    [InlineData("2026-12-21T12:00:00+01:00", "08:22", "16:20", "09:00", "16:00")] // winter solstice
    [InlineData("2026-06-21T12:00:00+02:00", "05:06", "21:37", "05:30", "21:30")] // summer solstice
    public void Day_scene_after_sunrise_and_night_scene_at_sunset_on_the_half_hour_grid(
        string day, string sunrise, string sunset, string dayStart, string nightStart)
    {
        SceneTimes times = SceneSchedule.For(DateTimeOffset.Parse(day), Latitude, Longitude, new SceneScheduleSettings());

        Assert.InRange((times.Sunrise.TimeOfDay - TimeSpan.Parse(sunrise)).Duration().TotalMinutes, 0, 6);
        Assert.InRange((times.Sunset.TimeOfDay - TimeSpan.Parse(sunset)).Duration().TotalMinutes, 0, 6);
        Assert.Equal(TimeOnly.Parse(dayStart), times.DayStart);
        Assert.Equal(TimeOnly.Parse(nightStart), times.NightStart);
    }
}
