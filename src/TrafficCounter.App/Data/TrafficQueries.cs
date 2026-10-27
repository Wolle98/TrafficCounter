using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TrafficCounter.Core;
using TrafficCounter.Core.Operations;
using TrafficCounter.Data;

namespace TrafficCounter.App.Data;

/// <summary>One counted vehicle, reduced to what the analysis needs (local time).</summary>
public readonly record struct PassageRow(DateTime At, TravelDirection Direction, VehicleClass Class, float? SpeedKmh);

/// <summary>One minute of operation: whether the camera delivered frames, and how good they were.</summary>
public readonly record struct MinuteRow(DateTime Start, SourceState Source, QualityLevel Quality, LightingCondition? Lighting);

public sealed record EventRow(DateTime At, SystemEventKind Kind, EventSeverity Severity, string Message);

/// <summary>Weather of one hour (ADR-0023), local time of the hour's start.</summary>
public readonly record struct WeatherRow(DateTime Hour, float? PrecipitationMm, float? TemperatureC, float? SunshineMinutes, string? Condition);

/// <summary>What a <see cref="DayMarker"/> is: a hand-written note or an imported holiday (ADR-0051).</summary>
public enum DayMarkerKind
{
    Note,
    PublicHoliday,
    SchoolHoliday,
}

/// <summary>A note or holiday over a span of local time; <paramref name="End"/> is exclusive.</summary>
public sealed record DayMarker(DateTime Start, DateTime End, DayMarkerKind Kind, string Text, Guid? NoteId = null)
{
    public bool Touches(DateTime from, DateTime to) => Start < to && End > from;

    /// <summary>Whole days only: starts and ends at midnight.</summary>
    public bool WholeDays => Start.TimeOfDay == TimeSpan.Zero && End.TimeOfDay == TimeSpan.Zero;
}

public sealed record PeriodRow(DateTime Started, DateTime? Ended, DateTime LastHeartbeat, string Machine, string? Model, string AppVersion);

/// <summary>The camera to analyse: the one that counted last (ADR-0024 data model).</summary>
public sealed record CameraInfo(int Id, string Name, string DirectionA, string DirectionB)
{
    public string DirectionName(TravelDirection direction) =>
        direction switch { TravelDirection.A => DirectionA, TravelDirection.B => DirectionB, _ => "unbekannt" };
}

/// <summary>
/// Queries of the analysis app (stage 4, ADR-0014); apart from notes on days (ADR-0051) they only read. Only rows counted by the service are used; hand
/// counts of reference videos (ADR-0027) stay out, they would count the same hours twice. Times are converted to
/// local time here, whatever the database stores (SQL Server local time, PostgreSQL UTC).
/// </summary>
public sealed class TrafficQueries(IDbContextFactory<TrafficCounterDbContext> factory)
{
    public async Task<CameraInfo?> CurrentCameraAsync(CancellationToken cancellationToken = default)
    {
        await using TrafficCounterDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        int? cameraId = await db.OperatingPeriods
            .Where(p => p.Source == DataSource.Counter)
            .OrderByDescending(p => p.LastHeartbeatAt)
            .Select(p => (int?)p.CameraId)
            .FirstOrDefaultAsync(cancellationToken);
        Camera? camera = cameraId is int id
            ? await db.Cameras.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            : await db.Cameras.OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);
        return camera is null ? null : new CameraInfo(camera.Id, camera.Name, camera.DirectionAName, camera.DirectionBName);
    }

    /// <summary>Local day of the first counting period of any camera; null while nothing was counted.</summary>
    public async Task<DateTime?> FirstMeasurementDayAsync(CancellationToken cancellationToken = default)
    {
        await using TrafficCounterDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset? first = await db.OperatingPeriods
            .Where(p => p.Source == DataSource.Counter)
            .OrderBy(p => p.StartedAt)
            .Select(p => (DateTimeOffset?)p.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return first?.ToLocalTime().Date;
    }

    public async Task<List<PassageRow>> PassagesAsync(int cameraId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        (DateTimeOffset start, DateTimeOffset end) = Range(from, to);
        await using TrafficCounterDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.VehiclePassages
            .Where(p => p.CameraId == cameraId && p.Source == DataSource.Counter && p.PassedAt >= start && p.PassedAt < end)
            .Select(p => new { p.PassedAt, p.Direction, p.VehicleClass, p.SpeedKmh })
            .ToListAsync(cancellationToken);
        return [.. rows
            .Select(p => new PassageRow(p.PassedAt.ToLocalTime().DateTime, p.Direction, p.VehicleClass ?? VehicleClass.Unknown, p.SpeedKmh))
            .OrderBy(p => p.At)];
    }

    public async Task<List<MinuteRow>> MinutesAsync(int cameraId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        (DateTimeOffset start, DateTimeOffset end) = Range(from, to);
        await using TrafficCounterDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.MinuteStatistics
            .Where(m => m.CameraId == cameraId && m.MinuteStart >= start && m.MinuteStart < end)
            .Select(m => new { m.MinuteStart, m.SourceState, m.Quality, m.Lighting })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(m => new MinuteRow(m.MinuteStart.ToLocalTime().DateTime, m.SourceState, m.Quality, m.Lighting)).OrderBy(m => m.Start)];
    }

    public async Task<List<EventRow>> EventsAsync(int cameraId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        (DateTimeOffset start, DateTimeOffset end) = Range(from, to);
        await using TrafficCounterDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.SystemEvents
            .Where(e => e.CameraId == cameraId && e.OccurredAt >= start && e.OccurredAt < end)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new { e.OccurredAt, e.Kind, e.Severity, e.Message })
            .Take(500)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(e => new EventRow(e.OccurredAt.ToLocalTime().DateTime, e.Kind, e.Severity, e.Message))];
    }

    public async Task<List<PeriodRow>> PeriodsAsync(int cameraId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        (DateTimeOffset start, DateTimeOffset end) = Range(from, to);
        await using TrafficCounterDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.OperatingPeriods
            .Where(p => p.CameraId == cameraId && p.Source == DataSource.Counter && p.LastHeartbeatAt >= start && p.StartedAt < end)
            .OrderByDescending(p => p.StartedAt)
            .Select(p => new { p.StartedAt, p.EndedAt, p.LastHeartbeatAt, p.MachineName, p.ModelName, p.AppVersion })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(p => new PeriodRow(
            p.StartedAt.ToLocalTime().DateTime, p.EndedAt?.ToLocalTime().DateTime, p.LastHeartbeatAt.ToLocalTime().DateTime, p.MachineName, p.ModelName, p.AppVersion))];
    }

    /// <summary>Hourly weather of the station with the most recent import (there is normally only one).</summary>
    public async Task<List<WeatherRow>> WeatherAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        (DateTimeOffset start, DateTimeOffset end) = Range(from, to);
        await using TrafficCounterDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        try
        {
            string? station = await db.WeatherHours.OrderByDescending(w => w.ImportedAt).Select(w => w.StationId).FirstOrDefaultAsync(cancellationToken);
            if (station is null)
            {
                return [];
            }

            var rows = await db.WeatherHours
                .Where(w => w.StationId == station && w.Hour >= start && w.Hour < end)
                .Select(w => new { w.Hour, w.PrecipitationMm, w.TemperatureC, w.SunshineMinutes, w.Condition })
                .ToListAsync(cancellationToken);
            return [.. rows.Select(w => new WeatherRow(w.Hour.ToLocalTime().DateTime, w.PrecipitationMm, w.TemperatureC, w.SunshineMinutes, w.Condition)).OrderBy(w => w.Hour)];
        }
        catch (DbException)
        {
            // Database without the weather table yet (migration AddWeatherHour not applied): no weather.
            return [];
        }
    }

    /// <summary>Notes and holidays that touch [<paramref name="from"/>, <paramref name="to"/>), holidays first.</summary>
    public async Task<List<DayMarker>> DayMarkersAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        (DateTimeOffset start, DateTimeOffset end) = Range(from, to);
        await using TrafficCounterDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        try
        {
            DateOnly firstDay = DateOnly.FromDateTime(from);
            DateOnly lastDay = DateOnly.FromDateTime(to.AddTicks(-1));
            bool english = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
            List<DayMarker> markers = [.. (await db.CalendarEntries.AsNoTracking()
                .Where(c => c.StartDate <= lastDay && c.EndDate >= firstDay)
                .ToListAsync(cancellationToken))
                .DistinctBy(c => (c.Kind, c.StartDate, c.EndDate, c.Name))
                .OrderBy(c => c.StartDate)
                .Select(c => new DayMarker(
                    c.StartDate.ToDateTime(TimeOnly.MinValue), c.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue),
                    c.Kind == CalendarEntryKind.PublicHoliday ? DayMarkerKind.PublicHoliday : DayMarkerKind.SchoolHoliday,
                    english ? c.EnglishName ?? c.Name : c.Name))];
            markers.AddRange((await DayNotes.ListAsync(db, start, end, cancellationToken))
                .Select(n => new DayMarker(n.Start.ToLocalTime().DateTime, n.End.ToLocalTime().DateTime, DayMarkerKind.Note, n.Text, n.SyncId)));
            return markers;
        }
        catch (DbException)
        {
            // Database without the tables yet (migration AddHolidaysAndNotes not applied): nothing to show.
            return [];
        }
    }

    public async Task AddNoteAsync(DateTime start, DateTime end, string text, CancellationToken cancellationToken = default)
    {
        (DateTimeOffset from, DateTimeOffset to) = Range(start, end);
        await using TrafficCounterDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await DayNotes.AddAsync(db, from, to, text, cancellationToken);
    }

    public async Task<bool> UpdateNoteAsync(Guid id, DateTime start, DateTime end, string text, CancellationToken cancellationToken = default)
    {
        (DateTimeOffset from, DateTimeOffset to) = Range(start, end);
        await using TrafficCounterDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        return await DayNotes.UpdateAsync(db, id, from, to, text, cancellationToken);
    }

    public async Task<bool> DeleteNoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using TrafficCounterDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        return await DayNotes.DeleteAsync(db, id, cancellationToken);
    }

    private static (DateTimeOffset Start, DateTimeOffset End) Range(DateTime from, DateTime to) =>
        (new DateTimeOffset(DateTime.SpecifyKind(from, DateTimeKind.Local)).ToUniversalTime(),
         new DateTimeOffset(DateTime.SpecifyKind(to, DateTimeKind.Local)).ToUniversalTime());
}
