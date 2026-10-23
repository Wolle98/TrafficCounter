namespace TrafficCounter.Core.Operations;

/// <summary>Settings of the automatic day/night scene times of an IP camera (ADR-0042).</summary>
public sealed class SceneScheduleSettings
{
    /// <summary>Day scene this many minutes after sunrise: before that the short day exposure gives a too dark image.</summary>
    public double MorningDelayMinutes { get; set; } = 30;

    /// <summary>Night scene this many minutes before sunset (0: at sunset).</summary>
    public double EveningAdvanceMinutes { get; set; }

    /// <summary>Grid of the camera's schedule: the NC400 only works reliably with full and half hours.</summary>
    public int GridMinutes { get; set; } = 30;
}

/// <summary>Sunrise, sunset and the scene switch times derived from them, in local time of the given day.</summary>
public sealed record SceneTimes(DateTimeOffset Sunrise, DateTimeOffset Sunset, TimeOnly DayStart, TimeOnly NightStart);

/// <summary>
/// When an IP camera should switch from its night to its day scene and back on a given day (ADR-0042): the day scene
/// some time after sunrise (rounded to the nearest grid step, i.e. 15 to 45 minutes after sunrise with the defaults),
/// the night scene at sunset (rounded down, so the short day exposure does not run into the dusk).
/// </summary>
public static class SceneSchedule
{
    /// <summary>Sun at the horizon including refraction (standard sunrise/sunset definition).</summary>
    public const double HorizonDegrees = -0.833;

    /// <param name="noon">Any time of the day in question, with the local UTC offset of that day.</param>
    public static SceneTimes For(DateTimeOffset noon, double latitude, double longitude, SceneScheduleSettings settings)
    {
        var midnight = new DateTimeOffset(noon.Date, noon.Offset);
        DateTimeOffset sunrise = Crossing(midnight, midnight.AddHours(14), latitude, longitude, rising: true);
        DateTimeOffset sunset = Crossing(midnight.AddHours(10), midnight.AddDays(1), latitude, longitude, rising: false);
        int grid = Math.Max(1, settings.GridMinutes);
        double day = (sunrise - midnight).TotalMinutes + settings.MorningDelayMinutes;
        double night = (sunset - midnight).TotalMinutes - settings.EveningAdvanceMinutes;
        int dayStart = Math.Clamp((int)Math.Round(day / grid, MidpointRounding.AwayFromZero) * grid, grid, (24 * 60) - (2 * grid));
        int nightStart = Math.Clamp((int)Math.Floor(night / grid) * grid, dayStart + grid, (24 * 60) - grid);
        return new SceneTimes(sunrise, sunset, TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(dayStart)), TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(nightStart)));
    }

    /// <summary>First minute in [from, to) at which the sun crosses the horizon in the given direction.</summary>
    private static DateTimeOffset Crossing(DateTimeOffset from, DateTimeOffset to, double latitude, double longitude, bool rising)
    {
        double previous = SunPosition.ElevationDegrees(from, latitude, longitude);
        for (DateTimeOffset t = from.AddMinutes(1); t < to; t = t.AddMinutes(1))
        {
            double elevation = SunPosition.ElevationDegrees(t, latitude, longitude);
            if (rising ? previous < HorizonDegrees && elevation >= HorizonDegrees : previous > HorizonDegrees && elevation <= HorizonDegrees)
            {
                return t;
            }

            previous = elevation;
        }

        // Polar day or night: no crossing; fall back to the ends of the searched span.
        return rising ? from : to;
    }
}
