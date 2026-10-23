using Microsoft.EntityFrameworkCore;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.Data;

/// <summary>What one gap fill changed (or would change) in the target database.</summary>
public sealed record GapFillResult(int Minutes, int Ranges, int PassagesRemoved, int MinutesRemoved, SyncResult? Copied)
{
    public override string ToString() =>
        $"{Minutes} minutes in {Ranges} ranges: removed {PassagesRemoved} passages and {MinutesRemoved} minute rows of the impaired camera"
        + (Copied is { } c ? $", copied {c.PassagesAdded} passages and {c.MinutesAdded} minutes" : "");
}

/// <summary>
/// Fills the minutes in which the counting camera was not measuring (degraded or unavailable) with the data
/// of a second camera that counted the same street meanwhile, from its own database. Only minutes the second camera
/// measured itself are taken. The impaired camera's passages and minute rows of those minutes are removed first, so
/// nothing is counted twice; minutes already measured by any camera are left alone. The origin stays visible through
/// the operating period's machine name.
/// <para>
/// Minutes without any row in the target are only filled on request (<c>includeMissing</c>): a database that has not
/// been synchronised yet lacks the counting camera's latest minutes, and filling them would count twice once they
/// arrive.
/// </para>
/// </summary>
public static class GapFill
{
    public static async Task<GapFillResult> FillAsync(
        TrafficCounterDbContext source, TrafficCounterDbContext target, DateTimeOffset start, DateTimeOffset end, bool dryRun,
        CancellationToken cancellationToken, bool includeMissing = false)
    {
        // Machines of the second camera: their rows in the target are the ones copied in earlier fills.
        HashSet<string> sourceMachines = [.. await source.OperatingPeriods.AsNoTracking()
            .Where(p => p.StartedAt < end && (p.EndedAt ?? p.LastHeartbeatAt) >= start)
            .Select(p => p.MachineName).Distinct().ToListAsync(cancellationToken)];

        HashSet<DateTime> sourceMeasured = [.. (await source.MinuteStatistics.AsNoTracking()
            .Where(m => m.SourceState == SourceState.Ok && m.MinuteStart >= start && m.MinuteStart < end)
            .Select(m => m.MinuteStart).ToListAsync(cancellationToken)).Select(Minute)];

        var targetMinutes = await (from m in target.MinuteStatistics.AsNoTracking()
                                   join p in target.OperatingPeriods.AsNoTracking() on m.OperatingPeriodId equals p.Id
                                   where m.MinuteStart >= start && m.MinuteStart < end
                                   select new { m.Id, m.MinuteStart, m.SourceState, p.MachineName }).ToListAsync(cancellationToken);
        HashSet<DateTime> alreadyMeasured = [.. targetMinutes.Where(m => m.SourceState == SourceState.Ok).Select(m => Minute(m.MinuteStart))];

        HashSet<DateTime> notMeasured = [.. targetMinutes.Where(m => m.SourceState != SourceState.Ok).Select(m => Minute(m.MinuteStart))];
        HashSet<DateTime> withRow = [.. targetMinutes.Select(m => Minute(m.MinuteStart))];
        List<DateTime> fill = [.. sourceMeasured
            .Where(m => !alreadyMeasured.Contains(m) && (notMeasured.Contains(m) || (includeMissing && !withRow.Contains(m))))
            .Order()];
        HashSet<DateTime> fillSet = [.. fill];
        List<(DateTimeOffset From, DateTimeOffset To)> ranges = Ranges(fill);

        List<long> minuteRows = [.. targetMinutes
            .Where(m => !sourceMachines.Contains(m.MachineName) && fillSet.Contains(Minute(m.MinuteStart)))
            .Select(m => m.Id)];
        var passages = await (from v in target.VehiclePassages.AsNoTracking()
                              join p in target.OperatingPeriods.AsNoTracking() on v.OperatingPeriodId equals p.Id
                              where v.PassedAt >= start && v.PassedAt < end
                              select new { v.Id, v.PassedAt, p.MachineName }).ToListAsync(cancellationToken);
        List<long> passageRows = [.. passages
            .Where(v => !sourceMachines.Contains(v.MachineName) && fillSet.Contains(Minute(v.PassedAt)))
            .Select(v => v.Id)];

        if (dryRun || fill.Count == 0)
        {
            return new GapFillResult(fill.Count, ranges.Count, passageRows.Count, minuteRows.Count, null);
        }

        foreach (long[] chunk in passageRows.Chunk(500))
        {
            await target.VehiclePassages.Where(v => chunk.Contains(v.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        foreach (long[] chunk in minuteRows.Chunk(500))
        {
            await target.MinuteStatistics.Where(m => chunk.Contains(m.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        SyncResult total = new(0, 0, 0, 0, 0, 0, 0);
        foreach ((DateTimeOffset rangeFrom, DateTimeOffset rangeTo) in ranges)
        {
            SyncResult copied = await DatabaseSync.CopyAsync(source, target, (rangeFrom, rangeTo), cancellationToken);
            total = new SyncResult(
                total.CamerasAdded + copied.CamerasAdded, total.PeriodsAdded + copied.PeriodsAdded, total.PeriodsUpdated + copied.PeriodsUpdated,
                total.PassagesAdded + copied.PassagesAdded, total.MinutesAdded + copied.MinutesAdded,
                total.EventsAdded + copied.EventsAdded, total.DriftMeasurementsAdded + copied.DriftMeasurementsAdded);
        }

        return new GapFillResult(fill.Count, ranges.Count, passageRows.Count, minuteRows.Count, total);
    }

    /// <summary>The minute (UTC) a point in time falls into.</summary>
    private static DateTime Minute(DateTimeOffset time)
    {
        DateTime utc = time.UtcDateTime;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
    }

    /// <summary>Consecutive minutes merged into ranges [first, last + 1 minute).</summary>
    private static List<(DateTimeOffset From, DateTimeOffset To)> Ranges(List<DateTime> minutes)
    {
        List<(DateTimeOffset, DateTimeOffset)> ranges = [];
        for (int i = 0; i < minutes.Count;)
        {
            int j = i;
            while (j + 1 < minutes.Count && minutes[j + 1] == minutes[j].AddMinutes(1))
            {
                j++;
            }

            ranges.Add((new DateTimeOffset(minutes[i]), new DateTimeOffset(minutes[j].AddMinutes(1))));
            i = j + 1;
        }

        return ranges;
    }
}
