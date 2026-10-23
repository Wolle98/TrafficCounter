using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace TrafficCounter.Data;

/// <summary>What one synchronisation direction changed.</summary>
public sealed record SyncResult(
    int CamerasAdded,
    int PeriodsAdded,
    int PeriodsUpdated,
    int PassagesAdded,
    int MinutesAdded,
    int EventsAdded,
    int DriftMeasurementsAdded,
    int WeatherHoursChanged = 0)
{
    public override string ToString() =>
        $"cameras +{CamerasAdded}, periods +{PeriodsAdded} / updated {PeriodsUpdated}, passages +{PassagesAdded}, "
        + $"minutes +{MinutesAdded}, events +{EventsAdded}, drift +{DriftMeasurementsAdded}, weather {WeatherHoursChanged}";
}

/// <summary>
/// Copies counting data from one database to another, e.g. SQL Server on the development machine and PostgreSQL on
/// the NAS (ADR-0030). Rows are matched by <c>SyncId</c>, cameras by name. Missing rows are inserted with new local
/// keys; of an operating period present on both sides the copy with the newer <see cref="OperatingPeriod.UpdatedAt"/>
/// wins. A newer hand-counted recording replaces all passages of its period, because re-importing a video renews them.
/// Deletions are not propagated. Run it in both directions for a two-way sync.
/// </summary>
public static class DatabaseSync
{
    public static Task<SyncResult> CopyAsync(TrafficCounterDbContext source, TrafficCounterDbContext target, CancellationToken cancellationToken) =>
        CopyAsync(source, target, null, cancellationToken);

    /// <param name="range">
    /// Only rows in this time range (passages, minutes, events, drift measurements), and only the periods overlapping
    /// it; weather is left out. For filling a gap of one counter with another one's data, e.g. a second camera
    /// that counted the same street meanwhile. Copying the rest of such a source later would count twice.
    /// </param>
    public static async Task<SyncResult> CopyAsync(
        TrafficCounterDbContext source, TrafficCounterDbContext target, (DateTimeOffset From, DateTimeOffset To)? range, CancellationToken cancellationToken)
    {
        // 1. Cameras by name.
        Dictionary<int, int> cameraIds = [];
        int camerasAdded = 0;
        foreach (Camera camera in await source.Cameras.AsNoTracking().ToListAsync(cancellationToken))
        {
            Camera? existing = await target.Cameras.SingleOrDefaultAsync(c => c.Name == camera.Name, cancellationToken);
            if (existing is null)
            {
                existing = Clone(camera);
                existing.Id = 0;
                target.Cameras.Add(existing);
                await target.SaveChangesAsync(cancellationToken);
                camerasAdded++;
            }

            cameraIds[camera.Id] = existing.Id;
        }

        // 2. Operating periods: insert missing ones, update older copies.
        Dictionary<Guid, OperatingPeriod> targetPeriods = await target.OperatingPeriods.ToDictionaryAsync(p => p.SyncId, cancellationToken);
        List<(OperatingPeriod Source, OperatingPeriod Target, bool ReplaceChildren)> periods = [];
        int periodsAdded = 0;
        int periodsUpdated = 0;
        IQueryable<OperatingPeriod> sourcePeriods = source.OperatingPeriods.AsNoTracking();
        if (range is { } periodRange)
        {
            DateTimeOffset rangeFrom = periodRange.From, rangeTo = periodRange.To;
            sourcePeriods = sourcePeriods.Where(p => p.StartedAt < rangeTo && (p.EndedAt ?? p.LastHeartbeatAt) >= rangeFrom);
        }

        foreach (OperatingPeriod period in await sourcePeriods.OrderBy(p => p.StartedAt).ToListAsync(cancellationToken))
        {
            if (!targetPeriods.TryGetValue(period.SyncId, out OperatingPeriod? copy))
            {
                copy = Clone(period);
                copy.Id = 0;
                copy.CameraId = cameraIds[period.CameraId];
                target.OperatingPeriods.Add(copy);
                periodsAdded++;
                periods.Add((period, copy, false));
            }
            else if (period.UpdatedAt > copy.UpdatedAt)
            {
                int id = copy.Id;
                CopyValues(period, copy);
                copy.Id = id;
                copy.CameraId = cameraIds[period.CameraId];
                periodsUpdated++;
                periods.Add((period, copy, period.Source == DataSource.Annotation));
            }
            else
            {
                periods.Add((period, copy, false));
            }
        }

        await target.SaveChangesAsync(cancellationToken);

        // 3. Rows of the periods: per table only the keys of both sides, full rows only where some are missing.
        foreach (OperatingPeriod targetPeriod in periods.Where(p => p.ReplaceChildren).Select(p => p.Target))
        {
            await target.VehiclePassages.Where(p => p.OperatingPeriodId == targetPeriod.Id).ExecuteDeleteAsync(cancellationToken);
        }

        Dictionary<int, OperatingPeriod> bySourcePeriod = periods.ToDictionary(p => p.Source.Id, p => p.Target);
        int passages = await CopyRowsAsync(target, source.VehiclePassages, target.VehiclePassages, bySourcePeriod,
            (p, c, t) => { p.Id = 0; p.CameraId = c; p.OperatingPeriodId = t; }, range, nameof(VehiclePassage.PassedAt), cancellationToken);
        int minutes = await CopyRowsAsync(target, source.MinuteStatistics, target.MinuteStatistics, bySourcePeriod,
            (m, c, t) => { m.Id = 0; m.CameraId = c; m.OperatingPeriodId = t; }, range, nameof(MinuteStatistic.MinuteStart), cancellationToken);
        int events = await CopyRowsAsync(target, source.SystemEvents, target.SystemEvents, bySourcePeriod,
            (e, c, t) => { e.Id = 0; e.CameraId = c; e.OperatingPeriodId = t; }, range, nameof(SystemEvent.OccurredAt), cancellationToken);
        int drift = await CopyRowsAsync(target, source.DriftMeasurements, target.DriftMeasurements, bySourcePeriod,
            (d, c, t) => { d.Id = 0; d.CameraId = c; d.OperatingPeriodId = t; }, range, nameof(DriftMeasurement.MeasuredAt), cancellationToken);
        target.ChangeTracker.Clear();

        // 4. Weather hours (ADR-0023), by station and hour; the newer import wins. Not when copying a time range.
        int weather = 0;
        List<WeatherHour> weatherHours = range is null ? await source.WeatherHours.AsNoTracking().ToListAsync(cancellationToken) : [];
        foreach (IGrouping<string, WeatherHour> station in weatherHours.GroupBy(w => w.StationId))
        {
            // Compared per hour, not against the newest import of the station: a later backfill of older hours
            // on one side has an older ImportedAt than the regular imports on the other and would be lost.
            Dictionary<DateTimeOffset, DateTimeOffset> imported = (await target.WeatherHours.AsNoTracking()
                .Where(w => w.StationId == station.Key)
                .Select(w => new { w.Hour, w.ImportedAt })
                .ToListAsync(cancellationToken))
                .ToDictionary(w => w.Hour.ToUniversalTime(), w => w.ImportedAt);
            List<WeatherHour> newer = [.. station
                .Where(w => !imported.TryGetValue(w.Hour.ToUniversalTime(), out DateTimeOffset importedAt) || w.ImportedAt > importedAt)
                .Select(w => new WeatherHour
                {
                    StationId = w.StationId, Hour = w.Hour, PrecipitationMm = w.PrecipitationMm, TemperatureC = w.TemperatureC,
                    SunshineMinutes = w.SunshineMinutes, WindSpeedKmh = w.WindSpeedKmh, CloudCoverPercent = w.CloudCoverPercent,
                    VisibilityMeters = w.VisibilityMeters, Condition = w.Condition, ImportedAt = w.ImportedAt,
                })];
            weather += await WeatherImport.StoreAsync(target, newer, cancellationToken);
            target.ChangeTracker.Clear();
        }

        return new SyncResult(camerasAdded, periodsAdded, periodsUpdated, passages, minutes, events, drift, weather);
    }

    /// <summary>Stable id of a hand-counted recording, so re-imports on either side refer to the same period.</summary>
    public static Guid AnnotationPeriodId(string videoName)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("TrafficCounter annotation period:" + videoName.ToUpperInvariant()));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// Copies the rows of the given periods that the target lacks (by <c>SyncId</c>). Only the keys are compared,
    /// with one query per side; full rows are read only for periods with missing rows. All row tables have the
    /// columns <c>SyncId</c> and <c>OperatingPeriodId</c>.
    /// </summary>
    private static async Task<int> CopyRowsAsync<T>(
        TrafficCounterDbContext target,
        DbSet<T> sourceSet,
        DbSet<T> targetSet,
        Dictionary<int, OperatingPeriod> bySourcePeriod,
        Action<T, int, int> remap,
        (DateTimeOffset From, DateTimeOffset To)? range,
        string timeColumn,
        CancellationToken cancellationToken)
        where T : class
    {
        IQueryable<T> InRange(IQueryable<T> rows) => range is { } r
            ? rows.Where(x => EF.Property<DateTimeOffset>(x, timeColumn) >= r.From && EF.Property<DateTimeOffset>(x, timeColumn) < r.To)
            : rows;

        HashSet<Guid> existing = [.. await InRange(targetSet.AsNoTracking())
            .Select(x => EF.Property<Guid>(x, "SyncId")).ToListAsync(cancellationToken)];
        var keys = await InRange(sourceSet.AsNoTracking())
            .Select(x => new { Period = EF.Property<int>(x, "OperatingPeriodId"), SyncId = EF.Property<Guid>(x, "SyncId") })
            .ToListAsync(cancellationToken);

        PropertyInfo syncId = typeof(T).GetProperty("SyncId")!;
        int copied = 0;
        foreach (IGrouping<int, Guid> period in keys
            .Where(k => !existing.Contains(k.SyncId) && bySourcePeriod.ContainsKey(k.Period))
            .GroupBy(k => k.Period, k => k.SyncId))
        {
            HashSet<Guid> missing = [.. period];
            OperatingPeriod targetPeriod = bySourcePeriod[period.Key];
            List<T> rows = await InRange(sourceSet.AsNoTracking())
                .Where(x => EF.Property<int>(x, "OperatingPeriodId") == period.Key)
                .ToListAsync(cancellationToken);
            foreach (T row in rows.Where(r => missing.Contains((Guid)syncId.GetValue(r)!)))
            {
                T copy = Clone(row);
                remap(copy, targetPeriod.CameraId, targetPeriod.Id);
                targetSet.Add(copy);
                copied++;
            }

            await target.SaveChangesAsync(cancellationToken);
            target.ChangeTracker.Clear();
        }

        return copied;
    }

    /// <summary>Shallow copy of an entity; created without constructor because the entities have required members.</summary>
    private static T Clone<T>(T source)
        where T : class
    {
        var copy = (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));
        CopyValues(source, copy);
        return copy;
    }

    private static void CopyValues<T>(T source, T target)
    {
        foreach (PropertyInfo property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Only column values; navigation properties such as OperatingPeriod.Camera stay untouched.
            bool column = property.PropertyType.IsValueType || property.PropertyType == typeof(string);
            if (column && property.CanRead && property.CanWrite)
            {
                property.SetValue(target, property.GetValue(source));
            }
        }
    }
}
