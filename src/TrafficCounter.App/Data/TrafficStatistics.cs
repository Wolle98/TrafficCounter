using TrafficCounter.Core;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.App.Data;

/// <summary>Count and measured minutes of one time bucket; the rate per hour accounts for gaps (ADR-0024).</summary>
/// <param name="Count">All counted vehicles in the bucket.</param>
/// <param name="MeasuredCount">Vehicles counted in fully measured minutes, the basis of the rate.</param>
public readonly record struct Bucket(DateTime Start, int Count, int CoveredMinutes, int Minutes, int MeasuredCount)
{
    /// <summary>
    /// Vehicles per hour in the measured minutes, or null if none. Vehicles of minutes that do not count as measured
    /// (e.g. a degraded stream, ADR-0024) are left out, otherwise they would raise the rate of the others.
    /// </summary>
    public double? RatePerHour => CoveredMinutes > 0 ? MeasuredCount * 60.0 / CoveredMinutes : null;

    public double Coverage => Minutes > 0 ? (double)CoveredMinutes / Minutes : 0;
}

/// <summary>Speed figures of a set of passages (stage 5, statistical estimates, ADR-0033).</summary>
public sealed record SpeedSummary(int Count, double Median, double V85, double ShareOver30, double ShareOver35, double ShareOver40, double ShareOver50);

/// <summary>Aggregations for the analysis pages; pure functions over the rows from <see cref="TrafficQueries"/>.</summary>
public static class TrafficStatistics
{
    /// <summary>
    /// Display order and colours of the vehicle classes shown (ADR-0013); the names come from the UI texts
    /// (<c>Class.*</c>). Vans and e-scooters are only recognised by the own model (stage 3); with the COCO model they
    /// stay empty (vans counted as cars or trucks, e-scooters not at all).
    /// </summary>
    public static IReadOnlyList<(VehicleClass Class, string Color)> Classes { get; } =
    [
        (VehicleClass.Car, "#4e79a7"),
        (VehicleClass.Van, "#76b7b2"),
        (VehicleClass.Truck, "#e15759"),
        (VehicleClass.Bus, "#f28e2b"),
        (VehicleClass.Agricultural, "#8c6d31"),
        (VehicleClass.Motorcycle, "#b07aa1"),
        (VehicleClass.Bicycle, "#59a14f"),
        (VehicleClass.EScooter, "#edc948"),
        (VehicleClass.Unknown, "#9c9c9c"),
    ];

    /// <summary>Motor vehicles, for speed figures (cyclists would lower them).</summary>
    public static bool IsMotorVehicle(VehicleClass vehicleClass) =>
        vehicleClass is VehicleClass.Car or VehicleClass.Van or VehicleClass.Truck or VehicleClass.Bus
            or VehicleClass.Motorcycle or VehicleClass.Agricultural;

    /// <summary>Minutes in which the camera delivered frames.</summary>
    public static HashSet<DateTime> CoveredMinutes(IEnumerable<MinuteRow> minutes) =>
        [.. minutes.Where(m => m.Source == SourceState.Ok).Select(m => Truncate(m.Start, TimeSpan.FromMinutes(1)))];

    /// <summary>Buckets of <paramref name="size"/> (an hour or a day) from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static List<Bucket> Buckets(
        IEnumerable<PassageRow> passages, HashSet<DateTime> covered, DateTime from, DateTime to, TimeSpan size, DateTime? now = null)
    {
        List<PassageRow> all = [.. passages];
        Dictionary<DateTime, int> counts = all
            .GroupBy(p => Truncate(p.At, size))
            .ToDictionary(g => g.Key, g => g.Count());
        Dictionary<DateTime, int> measured = Measured(all, covered)
            .GroupBy(p => Truncate(p.At, size))
            .ToDictionary(g => g.Key, g => g.Count());
        Dictionary<DateTime, int> coveredPerBucket = covered
            .GroupBy(m => Truncate(m, size))
            .ToDictionary(g => g.Key, g => g.Count());

        List<Bucket> buckets = [];
        DateTime limit = now is DateTime n && n < to ? n : to;
        for (DateTime start = Truncate(from, size); start < to; start += size)
        {
            // Minutes that have passed (for the current bucket only those up to now).
            DateTime end = start + size < limit ? start + size : limit;
            int minutes = end > start ? (int)(end - start).TotalMinutes : 0;
            buckets.Add(new Bucket(start, counts.GetValueOrDefault(start), coveredPerBucket.GetValueOrDefault(start), minutes, measured.GetValueOrDefault(start)));
        }

        return buckets;
    }

    /// <summary>
    /// Usual vehicles per hour for each hour of the day on the given weekday, from the measured minutes of the
    /// days in the range: the comparison line on the overview.
    /// </summary>
    public static double?[] TypicalDay(IEnumerable<PassageRow> passages, HashSet<DateTime> covered, DayOfWeek day)
    {
        int[] counts = new int[24];
        int[] minutes = new int[24];
        foreach (PassageRow p in Measured(passages, covered).Where(p => p.At.DayOfWeek == day))
        {
            counts[p.At.Hour]++;
        }

        foreach (DateTime m in covered.Where(m => m.DayOfWeek == day))
        {
            minutes[m.Hour]++;
        }

        return [.. Enumerable.Range(0, 24).Select(h => minutes[h] >= 30 ? counts[h] * 60.0 / minutes[h] : (double?)null)];
    }

    /// <summary>Average vehicles per hour by weekday (Monday first) and hour; null where nothing was measured.</summary>
    public static double?[,] WeekHeatmap(IEnumerable<PassageRow> passages, HashSet<DateTime> covered)
    {
        int[,] counts = new int[7, 24];
        int[,] minutes = new int[7, 24];
        foreach (PassageRow p in Measured(passages, covered))
        {
            counts[DayIndex(p.At), p.At.Hour]++;
        }

        foreach (DateTime m in covered)
        {
            minutes[DayIndex(m), m.Hour]++;
        }

        var result = new double?[7, 24];
        for (int d = 0; d < 7; d++)
        {
            for (int h = 0; h < 24; h++)
            {
                result[d, h] = minutes[d, h] >= 30 ? counts[d, h] * 60.0 / minutes[d, h] : null;
            }
        }

        return result;
    }

    /// <summary>
    /// Vehicles per hour for every date of one weekday in the range (rows, newest first) and hour (columns), to compare
    /// the same weekday across weeks; null where less than half an hour was measured.
    /// </summary>
    public static (IReadOnlyList<DateTime> Dates, double?[,] Values) DayHeatmap(
        IEnumerable<PassageRow> passages, HashSet<DateTime> covered, DayOfWeek day, DateTime from, DateTime to)
    {
        List<DateTime> dates = [];
        for (DateTime date = to.Date.AddDays(-1); date >= from.Date; date = date.AddDays(-1))
        {
            if (date.DayOfWeek == day)
            {
                dates.Add(date);
            }
        }

        Dictionary<DateTime, int> row = dates.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        int[,] counts = new int[dates.Count, 24];
        int[,] minutes = new int[dates.Count, 24];
        foreach (PassageRow p in Measured(passages, covered))
        {
            if (row.TryGetValue(p.At.Date, out int r))
            {
                counts[r, p.At.Hour]++;
            }
        }

        foreach (DateTime m in covered)
        {
            if (row.TryGetValue(m.Date, out int r))
            {
                minutes[r, m.Hour]++;
            }
        }

        var values = new double?[dates.Count, 24];
        for (int r = 0; r < dates.Count; r++)
        {
            for (int h = 0; h < 24; h++)
            {
                values[r, h] = minutes[r, h] >= 30 ? counts[r, h] * 60.0 / minutes[r, h] : null;
            }
        }

        return (dates, values);
    }

    public static int DayIndex(DateTime t) => ((int)t.DayOfWeek + 6) % 7;

    /// <summary>Passages in measured minutes: rates divide by the measured minutes, so only their vehicles count.</summary>
    private static IEnumerable<PassageRow> Measured(IEnumerable<PassageRow> passages, HashSet<DateTime> covered) =>
        passages.Where(p => covered.Contains(Truncate(p.At, TimeSpan.FromMinutes(1))));

    public static SpeedSummary? Speeds(IEnumerable<PassageRow> passages)
    {
        List<double> speeds = [.. passages.Where(p => p.SpeedKmh is > 0 && IsMotorVehicle(p.Class)).Select(p => (double)p.SpeedKmh!.Value).Order()];
        if (speeds.Count == 0)
        {
            return null;
        }

        return new SpeedSummary(
            speeds.Count,
            Percentile(speeds, 0.5),
            Percentile(speeds, 0.85),
            speeds.Count(s => s > 30) / (double)speeds.Count,
            speeds.Count(s => s > 35) / (double)speeds.Count,
            speeds.Count(s => s > 40) / (double)speeds.Count,
            speeds.Count(s => s > 50) / (double)speeds.Count);
    }

    /// <summary>Number of motor vehicles per speed band of <paramref name="width"/> km/h, up to <paramref name="max"/>.</summary>
    public static int[] SpeedHistogram(IEnumerable<PassageRow> passages, int width = 5, int max = 70)
    {
        int[] bins = new int[(max / width) + 1];
        foreach (PassageRow p in passages.Where(p => p.SpeedKmh is > 0 && IsMotorVehicle(p.Class)))
        {
            bins[Math.Min(bins.Length - 1, (int)(p.SpeedKmh!.Value / width))]++;
        }

        return bins;
    }

    /// <summary>Linear interpolation between the closest ranks of sorted values.</summary>
    public static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        double rank = p * (sorted.Count - 1);
        int lower = (int)Math.Floor(rank);
        int upper = Math.Min(sorted.Count - 1, lower + 1);
        return sorted[lower] + ((rank - lower) * (sorted[upper] - sorted[lower]));
    }

    public static DateTime Truncate(DateTime t, TimeSpan size) =>
        size >= TimeSpan.FromDays(1) ? t.Date : new DateTime(t.Ticks - (t.Ticks % size.Ticks), t.Kind);
}
