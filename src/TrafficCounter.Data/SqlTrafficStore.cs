using Microsoft.EntityFrameworkCore;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.Data;

/// <summary>EF Core implementation of <see cref="ITrafficStore"/> for SQL Server and PostgreSQL (ADR-0010, ADR-0024, ADR-0030).</summary>
public sealed class SqlTrafficStore(IDbContextFactory<TrafficCounterDbContext> contextFactory, TimeProvider? timeProvider = null)
    : ITrafficStore
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<int> EnsureCameraAsync(CameraRegistration registration, CancellationToken cancellationToken)
    {
        await using TrafficCounterDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        Camera? camera = await db.Cameras.SingleOrDefaultAsync(c => c.Name == registration.Name, cancellationToken);
        if (camera is null)
        {
            camera = new Camera { Name = registration.Name, CreatedAt = _time.GetLocalNow() };
            db.Cameras.Add(camera);
        }

        camera.DirectionAName = registration.DirectionAName;
        camera.DirectionBName = registration.DirectionBName;
        await db.SaveChangesAsync(cancellationToken);
        return camera.Id;
    }

    public async Task<(int PeriodId, int UncleanPeriodsClosed)> StartPeriodAsync(
        int cameraId, OperatingPeriodStart start, CancellationToken cancellationToken)
    {
        await using TrafficCounterDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Only this machine's own periods: one still running on another machine (e.g. a second camera whose
        // data filled a gap here) is not this one's crash.
        List<OperatingPeriod> open = await db.OperatingPeriods
            .Where(p => p.CameraId == cameraId && p.EndedAt == null && p.MachineName == start.MachineName)
            .ToListAsync(cancellationToken);

        var period = new OperatingPeriod
        {
            CameraId = cameraId,
            StartedAt = start.StartedAt,
            LastHeartbeatAt = start.StartedAt,
            NominalFps = (float)start.NominalFps,
            AppVersion = start.AppVersion,
            MachineName = start.MachineName,
            ModelName = start.ModelName,
            ConfigHash = start.ConfigHash,
        };
        db.OperatingPeriods.Add(period);
        await db.SaveChangesAsync(cancellationToken);

        foreach (OperatingPeriod stale in open)
        {
            stale.EndedAt = stale.LastHeartbeatAt;
            stale.EndReason = PeriodEndReason.Unclean;
            db.SystemEvents.Add(new SystemEvent
            {
                CameraId = cameraId,
                OperatingPeriodId = period.Id,
                OccurredAt = start.StartedAt,
                Kind = SystemEventKind.UncleanShutdownDetected,
                Severity = EventSeverity.Warning,
                Message = $"Operating period {stale.Id} was not ended properly; closed at its last heartbeat {stale.LastHeartbeatAt:O}.",
            });
        }

        db.SystemEvents.Add(new SystemEvent
        {
            CameraId = cameraId,
            OperatingPeriodId = period.Id,
            OccurredAt = start.StartedAt,
            Kind = SystemEventKind.Started,
            Severity = EventSeverity.Information,
            Message = $"Started {start.AppVersion} on {start.MachineName}, model {start.ModelName ?? "-"}, {start.NominalFps:0.#} fps.",
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (period.Id, open.Count);
    }

    public async Task EndPeriodAsync(int periodId, DateTimeOffset endedAt, PeriodEndReason reason, CancellationToken cancellationToken)
    {
        await using TrafficCounterDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        OperatingPeriod period = await db.OperatingPeriods.SingleAsync(p => p.Id == periodId, cancellationToken);
        period.EndedAt = endedAt;
        period.EndReason = reason;
        if (endedAt > period.LastHeartbeatAt)
        {
            period.LastHeartbeatAt = endedAt;
        }

        db.SystemEvents.Add(new SystemEvent
        {
            CameraId = period.CameraId,
            OperatingPeriodId = periodId,
            OccurredAt = endedAt,
            Kind = SystemEventKind.Stopped,
            Severity = reason == PeriodEndReason.Error ? EventSeverity.Error : EventSeverity.Information,
            Message = $"Stopped ({reason}).",
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(int Hour, float Contrast, float Sharpness, int Samples)>> GetQualityHistoryAsync(
        int cameraId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using TrafficCounterDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        // Grouped by local hour here, not in SQL: PostgreSQL returns UTC (ADR-0030). At most 14 days of minutes.
        var rows = await db.MinuteStatistics
            .Where(m => m.CameraId == cameraId && m.MinuteStart >= since && m.FramesProcessed > 0
                && m.Contrast != null && m.Sharpness != null
                && (m.Quality == QualityLevel.Good || m.Quality == QualityLevel.Unknown))
            .Select(m => new { m.MinuteStart, Contrast = m.Contrast!.Value, Sharpness = m.Sharpness!.Value })
            .ToListAsync(cancellationToken);
        return
        [
            .. rows.GroupBy(r => r.MinuteStart.ToLocalTime().Hour)
                .Select(g => (g.Key, g.Average(r => r.Contrast), g.Average(r => r.Sharpness), g.Count())),
        ];
    }

    public async Task WriteAsync(IReadOnlyList<StoreRecord> records, CancellationToken cancellationToken)
    {
        await using TrafficCounterDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        Dictionary<int, DateTimeOffset> heartbeats = [];

        foreach (StoreRecord record in records)
        {
            switch (record)
            {
                case PassageRecord p:
                    db.VehiclePassages.Add(new VehiclePassage
                    {
                        CameraId = p.CameraId,
                        OperatingPeriodId = p.OperatingPeriodId,
                        PassedAt = p.Passage.PassedAt,
                        Direction = p.Passage.Direction,
                        VehicleClass = p.Passage.Class,
                        ClassConfidence = p.Passage.ClassConfidence,
                        SpeedKmh = p.Passage.SpeedKmh,
                        LinePosition = p.Passage.LinePosition,
                        TrackId = p.Passage.TrackId,
                        TrackLengthFrames = p.Passage.TrackLengthFrames,
                        TrailerCount = (byte)Math.Clamp(p.Passage.TrailerCount, 0, byte.MaxValue),
                        Lighting = p.Lighting,
                    });
                    break;

                case MinuteStatistics m:
                    db.MinuteStatistics.Add(new MinuteStatistic
                    {
                        CameraId = m.CameraId,
                        OperatingPeriodId = m.OperatingPeriodId,
                        MinuteStart = m.MinuteStart,
                        FramesCaptured = m.FramesCaptured,
                        FramesProcessed = m.FramesProcessed,
                        FramesDropped = m.FramesDropped,
                        Detections = m.Detections,
                        Passages = m.Passages,
                        SourceState = m.SourceState,
                        Quality = m.Quality,
                        Brightness = m.Brightness,
                        Contrast = m.Contrast,
                        Sharpness = m.Sharpness,
                        Lighting = m.Lighting,
                    });
                    DateTimeOffset end = m.MinuteStart.AddMinutes(1);
                    if (!heartbeats.TryGetValue(m.OperatingPeriodId, out DateTimeOffset known) || end > known)
                    {
                        heartbeats[m.OperatingPeriodId] = end;
                    }

                    break;

                case SystemEventRecord e:
                    db.SystemEvents.Add(new SystemEvent
                    {
                        CameraId = e.CameraId,
                        OperatingPeriodId = e.OperatingPeriodId,
                        OccurredAt = e.OccurredAt,
                        Kind = e.Kind,
                        Severity = e.Severity,
                        Message = e.Message.Length > 1000 ? e.Message[..1000] : e.Message,
                    });
                    break;

                case DriftRecord d:
                    db.DriftMeasurements.Add(new DriftMeasurement
                    {
                        CameraId = d.CameraId,
                        OperatingPeriodId = d.OperatingPeriodId,
                        MeasuredAt = d.MeasuredAt,
                        Status = d.Status,
                        DxPixels = d.DxPixels,
                        DyPixels = d.DyPixels,
                        RotationDegrees = d.RotationDegrees,
                        Scale = d.Scale,
                        Matches = d.Matches,
                        InlierRatio = d.InlierRatio,
                    });
                    break;

                default:
                    throw new ArgumentException($"Unknown record type {record.GetType().Name}.", nameof(records));
            }
        }

        // Minute rows are the heartbeat (ADR-0024); never move it backwards (replayed fallback data may be older).
        foreach ((int periodId, DateTimeOffset heartbeat) in heartbeats)
        {
            OperatingPeriod? period = await db.OperatingPeriods.FindAsync([periodId], cancellationToken);
            if (period is null || heartbeat <= period.LastHeartbeatAt)
            {
                continue;
            }

            if (period.EndedAt is null)
            {
                period.LastHeartbeatAt = heartbeat;
            }
            else if (period.EndReason == PeriodEndReason.Unclean)
            {
                // Rows buffered during a database outage arrive after the crashed period was closed
                // at its last known heartbeat: they prove the period lasted longer.
                period.LastHeartbeatAt = heartbeat;
                period.EndedAt = heartbeat;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
