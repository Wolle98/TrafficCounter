using TrafficCounter.Core;

namespace TrafficCounter.DemoData;

/// <summary>
/// Synthetic traffic pattern of a residential street in a 30 km/h zone, with realistic example values: about 3,400
/// vehicles on a weekday, morning peak 07:30–08:00 with about 390/h, school traffic 13:00–14:30, evening peak
/// 16:30–17:00 with about 315/h, 150–250/h during the day, 60/h at 21:00 and 30/h at 23:00, quieter nights and
/// weekends.
/// </summary>
public static class TrafficProfile
{
    // Vehicles per hour (all classes) per half hour, 00:00 .. 23:30.
    private static readonly double[] Weekday =
    [
        20, 15, 10, 10, 6, 6, 5, 5, 6, 10, 25, 45,
        90, 160, 280, 390, 225, 195, 185, 180, 175, 160, 200, 245,
        155, 215, 260, 270, 235, 265, 250, 235, 215, 315, 280, 250,
        240, 220, 180, 150, 120, 90, 65, 55, 45, 40, 33, 25,
    ];

    // Weekends: a flatter profile scaled to the weekday level.
    private const double WeekendScale = 1.4;

    private static readonly double[] Saturday =
    [
        25, 20, 15, 12, 10, 8, 6, 5, 5, 5, 8, 10,
        15, 20, 30, 40, 55, 70, 90, 105, 120, 130, 140, 145,
        140, 130, 120, 115, 110, 110, 115, 120, 120, 120, 115, 110,
        100, 90, 80, 70, 60, 55, 45, 40, 35, 30, 28, 25,
    ];

    private static readonly double[] Sunday =
    [
        25, 20, 15, 12, 10, 8, 6, 5, 4, 4, 5, 6,
        8, 10, 15, 20, 30, 40, 55, 70, 90, 105, 115, 120,
        115, 105, 95, 95, 105, 115, 125, 130, 130, 125, 120, 110,
        100, 90, 80, 65, 55, 45, 35, 30, 22, 18, 14, 10,
    ];

    /// <summary>
    /// Target share of each class over the whole period. Buses are not drawn at random but run to
    /// <see cref="BusMinute"/>.
    /// </summary>
    public static IReadOnlyDictionary<VehicleClass, double> ClassShares { get; } = new Dictionary<VehicleClass, double>
    {
        [VehicleClass.Car] = 0.823,
        [VehicleClass.Van] = 0.045,
        [VehicleClass.Truck] = 0.015,
        [VehicleClass.Agricultural] = 0.002,
        [VehicleClass.Motorcycle] = 0.015,
        [VehicleClass.Bicycle] = 0.10,
    };

    /// <summary>Share of direction A over a day (39–50 % per hour, 44 % per day).</summary>
    public const double DirectionAShare = 0.44;

    /// <summary>
    /// Example bus timetable: every hour one bus in direction A around minute 15 and one in direction B around
    /// minute 45, on weekdays 06:00–19:59; at the weekend every two hours 08:00–18:59.
    /// </summary>
    public static int? BusMinute(DateTime local, TravelDirection direction)
    {
        bool runs = IsWeekend(local) ? local.Hour is >= 8 and < 19 && local.Hour % 2 == 0 : local.Hour is >= 6 and < 20;
        return runs ? (direction == TravelDirection.A ? 15 : 45) : null;
    }

    /// <summary>Base rate in vehicles per hour at a local time.</summary>
    public static double VehiclesPerHour(DateTime local)
    {
        int slot = (local.Hour * 2) + (local.Minute / 30);
        double rate = local.DayOfWeek switch
        {
            DayOfWeek.Saturday => Saturday[slot] * WeekendScale,
            DayOfWeek.Sunday => Sunday[slot] * WeekendScale,
            _ => Weekday[slot],
        };

        // Friday: going-out traffic in the late evening.
        return local.DayOfWeek == DayOfWeek.Friday && local.Hour >= 21 ? rate * 1.2 : rate;
    }

    public static bool IsWeekend(DateTime local) => local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>School traffic on weekdays: 07:15–08:00 and 13:00–14:30.</summary>
    public static bool IsSchoolPeak(DateTime local)
    {
        double hour = local.TimeOfDay.TotalHours;
        return !IsWeekend(local) && (hour is >= 7.25 and < 8 || hour is >= 13 and < 14.5);
    }

    /// <summary>Night for speed purposes: 22:00–05:00.</summary>
    public static bool IsNight(DateTime local) => local.Hour is >= 22 or < 5;

    /// <summary>
    /// Relative frequency of a class over the day (1 = follows the base rate). The generator normalises these so
    /// that <see cref="ClassShares"/> hold over the whole period.
    /// </summary>
    public static double ClassShape(VehicleClass vehicleClass, DateTime local)
    {
        double hour = local.TimeOfDay.TotalHours;
        bool weekend = IsWeekend(local);
        return vehicleClass switch
        {
            // Mostly during the day and early evening, rarely in the middle of the night; pupils at school times.
            VehicleClass.Bicycle => IsSchoolPeak(local) ? 1.6
                : hour < 5 ? 0.03
                : hour < 6 ? 0.3
                : hour < 7 ? 0.7
                : hour < 20 ? 1.0
                : hour < 21.5 ? 0.6
                : hour < 23 ? 0.2
                : 0.06,

            // Tractors during working hours, rarely on Sundays.
            VehicleClass.Agricultural => (hour is >= 7 and < 20 ? 1.0 : hour is >= 6 and < 22 ? 0.2 : 0)
                * (local.DayOfWeek == DayOfWeek.Sunday ? 0.1 : 1),

            // Sunday driving ban for heavy trucks, little delivery traffic at night.
            VehicleClass.Truck => (local.DayOfWeek == DayOfWeek.Sunday ? 0.1 : local.DayOfWeek == DayOfWeek.Saturday ? 0.4 : 1)
                * (hour is >= 6 and < 20 ? 1 : 0.4),
            VehicleClass.Van => (weekend ? 0.5 : 1) * (hour is >= 6 and < 20 ? 1 : 0.5),

            // Leisure rides at the weekend.
            VehicleClass.Motorcycle => weekend && hour is >= 10 and < 19 ? 1.6 : IsNight(local) ? 0.6 : 1,
            _ => 1,
        };
    }
}
