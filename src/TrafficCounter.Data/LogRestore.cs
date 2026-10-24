using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TrafficCounter.Core;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.Data;

/// <summary>A passage as the counting service logs it at debug level ("Passage track …").</summary>
public sealed record LoggedPassage(
    DateTimeOffset At, int TrackId, TravelDirection Direction, VehicleClass? Class, float Confidence, byte Trailers, float LinePosition, int Frames);

/// <summary>A minute statistic as the counting service logs it at debug level ("Minute hh:mm: …").</summary>
public sealed record LoggedMinute(
    DateTimeOffset Start, int Captured, int Processed, int Dropped, int Detections, int Passages, SourceState Source);

/// <summary>What a restore changed (or would change) in one database.</summary>
public sealed record LogRestoreResult(
    int Minutes, int PassagesRemoved, int MinutesRemoved, int PassagesAdded, int MinutesAdded, int DuplicatePassages = 0, int DuplicateMinutes = 0)
{
    public override string ToString() =>
        $"{Minutes} minutes: removed {PassagesRemoved} passages and {MinutesRemoved} minute rows of other machines, "
        + $"added {PassagesAdded} passages and {MinutesAdded} minute rows from the log"
        + (DuplicatePassages + DuplicateMinutes > 0 ? $"; removed duplicates: {DuplicatePassages} passages, {DuplicateMinutes} minute rows" : "");
}

/// <summary>
/// Puts the passages and minutes of a counting machine back from its service log, for minutes in which the
/// database no longer has them, e.g. after a gap fill replaced them with the counts of a second camera. The rows of
/// other machines in those minutes are removed, so nothing counts twice. The log has no speeds and no image quality:
/// restored passages have no speed, restored minutes an unknown quality. Minutes the machine still has in the
/// database are left alone, so a second run changes nothing.
/// <para>
/// Both databases are restored one after the other. The rows get the same sync ids in both, derived from machine,
/// time and track, so a later sync sees them as the same rows. A restore before that rule created them twice with
/// different ids, and the sync then copied each set into the other database; such duplicates of the machine (same
/// period and track at the same moment, or the same minute) are removed first, keeping the lower sync id in both.
/// </para>
/// </summary>
public static partial class LogRestore
{
    private static readonly string[] StampFormats = ["yyyy-MM-dd HH:mm:ss.fff zzz"];

    public static (List<LoggedPassage> Passages, List<LoggedMinute> Minutes) Parse(IEnumerable<string> lines)
    {
        List<LoggedPassage> passages = [];
        List<LoggedMinute> minutes = [];
        foreach (string line in lines)
        {
            if (line.Length < 30 || !DateTimeOffset.TryParseExact(line[..30], StampFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset stamp))
            {
                continue;
            }

            if (PassagePattern().Match(line) is { Success: true } p)
            {
                passages.Add(new LoggedPassage(
                    Before(stamp, TimeSpan.Parse(p.Groups["at"].Value, CultureInfo.InvariantCulture)),
                    int.Parse(p.Groups["track"].Value, CultureInfo.InvariantCulture),
                    p.Groups["dir"].Value == "A" ? TravelDirection.A : TravelDirection.B,
                    Enum.TryParse(p.Groups["class"].Value, out VehicleClass vehicleClass) ? vehicleClass : null,
                    float.Parse(p.Groups["conf"].Value, CultureInfo.InvariantCulture),
                    byte.Parse(p.Groups["trailers"].Value, CultureInfo.InvariantCulture),
                    float.Parse(p.Groups["pos"].Value, CultureInfo.InvariantCulture),
                    int.Parse(p.Groups["frames"].Value, CultureInfo.InvariantCulture)));
            }
            else if (MinutePattern().Match(line) is { Success: true } m)
            {
                int Int(string name) => int.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture);
                minutes.Add(new LoggedMinute(
                    Before(stamp, TimeSpan.Parse(m.Groups["minute"].Value, CultureInfo.InvariantCulture)),
                    Int("captured"), Int("processed"), Int("dropped"), Int("detections"), Int("passages"),
                    Enum.Parse<SourceState>(m.Groups["source"].Value)));
            }
        }

        return (passages, minutes);
    }

    /// <summary>The latest moment at <paramref name="timeOfDay"/> not after the log stamp (a minute logged after midnight).</summary>
    private static DateTimeOffset Before(DateTimeOffset stamp, TimeSpan timeOfDay)
    {
        var at = new DateTimeOffset(stamp.Date + timeOfDay, stamp.Offset);
        return at > stamp ? at.AddDays(-1) : at;
    }

    public static async Task<LogRestoreResult> RestoreAsync(
        TrafficCounterDbContext db, IReadOnlyList<LoggedPassage> passages, IReadOnlyList<LoggedMinute> minutes, string machine,
        DateTimeOffset start, DateTimeOffset end, LightingClassifier lighting, bool dryRun, CancellationToken cancellationToken)
    {
        (int duplicatePassages, int duplicateMinutes) = await RemoveDuplicatesAsync(db, machine, start, end, dryRun, cancellationToken);
        List<OperatingPeriod> periods = await db.OperatingPeriods.AsNoTracking()
            .Where(p => p.MachineName == machine && p.StartedAt < end && (p.EndedAt ?? p.LastHeartbeatAt) >= start)
            .ToListAsync(cancellationToken);
        OperatingPeriod? PeriodAt(DateTimeOffset time) =>
            periods.Where(p => p.StartedAt <= time && (p.EndedAt ?? p.LastHeartbeatAt) >= time)
                .OrderByDescending(p => p.StartedAt).FirstOrDefault();

        var rows = await (from m in db.MinuteStatistics.AsNoTracking()
                          join p in db.OperatingPeriods.AsNoTracking() on m.OperatingPeriodId equals p.Id
                          where m.MinuteStart >= start && m.MinuteStart < end
                          select new { m.Id, m.MinuteStart, p.MachineName }).ToListAsync(cancellationToken);
        HashSet<DateTimeOffset> present = [.. rows.Where(r => r.MachineName == machine).Select(r => r.MinuteStart)];

        // Minutes of the log the machine no longer has, and that fall into one of its periods.
        List<LoggedMinute> restore = [.. minutes
            .Where(m => m.Start >= start && m.Start < end && !present.Contains(m.Start) && PeriodAt(m.Start) is not null)
            .DistinctBy(m => m.Start)];
        HashSet<DateTimeOffset> restoreSet = [.. restore.Select(m => m.Start)];
        static DateTimeOffset Minute(DateTimeOffset t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Offset);

        List<long> otherMinutes = [.. rows.Where(r => r.MachineName != machine && restoreSet.Contains(r.MinuteStart)).Select(r => r.Id)];
        var otherPassages = (await (from v in db.VehiclePassages.AsNoTracking()
                                    join p in db.OperatingPeriods.AsNoTracking() on v.OperatingPeriodId equals p.Id
                                    where v.PassedAt >= start && v.PassedAt < end && p.MachineName != machine
                                    select new { v.Id, v.PassedAt }).ToListAsync(cancellationToken))
            .Where(v => restoreSet.Contains(Minute(v.PassedAt))).Select(v => v.Id).ToList();
        List<LoggedPassage> add = [.. passages.Where(p => restoreSet.Contains(Minute(p.At)))];

        if (!dryRun && restore.Count > 0)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.VehiclePassages.Where(v => otherPassages.Contains(v.Id)).ExecuteDeleteAsync(cancellationToken);
            await db.MinuteStatistics.Where(m => otherMinutes.Contains(m.Id)).ExecuteDeleteAsync(cancellationToken);
            foreach (LoggedMinute m in restore)
            {
                OperatingPeriod period = PeriodAt(m.Start)!;
                db.MinuteStatistics.Add(new MinuteStatistic
                {
                    SyncId = StableId($"log-minute|{machine}|{m.Start.UtcTicks}"),
                    CameraId = period.CameraId,
                    OperatingPeriodId = period.Id,
                    MinuteStart = m.Start,
                    FramesCaptured = m.Captured,
                    FramesProcessed = m.Processed,
                    FramesDropped = m.Dropped,
                    Detections = m.Detections,
                    Passages = m.Passages,
                    SourceState = m.Source,
                    Quality = QualityLevel.Unknown,
                    Lighting = lighting.Classify(m.Start),
                });
            }

            foreach (LoggedPassage p in add)
            {
                OperatingPeriod period = PeriodAt(p.At)!;
                db.VehiclePassages.Add(new VehiclePassage
                {
                    SyncId = StableId($"log-passage|{machine}|{p.TrackId}|{p.At.UtcTicks}"),
                    CameraId = period.CameraId,
                    OperatingPeriodId = period.Id,
                    PassedAt = p.At,
                    Direction = p.Direction,
                    VehicleClass = p.Class,
                    ClassConfidence = p.Confidence,
                    LinePosition = p.LinePosition,
                    TrackId = p.TrackId,
                    TrackLengthFrames = p.Frames,
                    TrailerCount = p.Trailers,
                    Source = DataSource.Counter,
                    Lighting = lighting.Classify(p.At),
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return new LogRestoreResult(restore.Count, otherPassages.Count, otherMinutes.Count, add.Count, restore.Count, duplicatePassages, duplicateMinutes);
    }

    private static Guid StableId(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes(key)));

    /// <summary>Rows of the machine that exist twice under different sync ids; keeps the lower id, in every database alike.</summary>
    private static async Task<(int Passages, int Minutes)> RemoveDuplicatesAsync(
        TrafficCounterDbContext db, string machine, DateTimeOffset start, DateTimeOffset end, bool dryRun, CancellationToken cancellationToken)
    {
        var passages = await (from v in db.VehiclePassages.AsNoTracking()
                              join p in db.OperatingPeriods.AsNoTracking() on v.OperatingPeriodId equals p.Id
                              where v.PassedAt >= start && v.PassedAt < end && p.MachineName == machine
                              select new { v.Id, v.SyncId, v.OperatingPeriodId, v.TrackId, v.PassedAt }).ToListAsync(cancellationToken);
        List<long> passageIds = [.. passages.GroupBy(v => (v.OperatingPeriodId, v.TrackId, v.PassedAt.UtcTicks))
            .SelectMany(g => g.OrderBy(v => v.SyncId.ToString("N"), StringComparer.Ordinal).Skip(1)).Select(v => v.Id)];
        var minutes = await (from m in db.MinuteStatistics.AsNoTracking()
                             join p in db.OperatingPeriods.AsNoTracking() on m.OperatingPeriodId equals p.Id
                             where m.MinuteStart >= start && m.MinuteStart < end && p.MachineName == machine
                             select new { m.Id, m.SyncId, m.OperatingPeriodId, m.MinuteStart }).ToListAsync(cancellationToken);
        List<long> minuteIds = [.. minutes.GroupBy(m => (m.OperatingPeriodId, m.MinuteStart.UtcTicks))
            .SelectMany(g => g.OrderBy(m => m.SyncId.ToString("N"), StringComparer.Ordinal).Skip(1)).Select(m => m.Id)];
        if (!dryRun)
        {
            await db.VehiclePassages.Where(v => passageIds.Contains(v.Id)).ExecuteDeleteAsync(cancellationToken);
            await db.MinuteStatistics.Where(m => minuteIds.Contains(m.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        return (passageIds.Count, minuteIds.Count);
    }

    [GeneratedRegex("""Passage track (?<track>\d+): "(?<dir>[AB])" "(?<class>\w*)" \((?<conf>[\d.]+)\), (?<trailers>\d+) trailer\(s\), line position (?<pos>[\d.]+), (?<frames>\d+) frames, at (?<at>\d\d:\d\d:\d\d\.\d+)""")]
    private static partial Regex PassagePattern();

    [GeneratedRegex("""Minute (?<minute>\d\d:\d\d): captured (?<captured>\d+), processed (?<processed>\d+), dropped (?<dropped>\d+), detections (?<detections>\d+), passages (?<passages>\d+), source "(?<source>\w+)""")]
    private static partial Regex MinutePattern();
}
