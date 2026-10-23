using Microsoft.EntityFrameworkCore;
using TrafficCounter.Core;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.Data;

/// <summary>One hand-counted passage of a recording, already mapped to the site's direction.</summary>
public sealed record AnnotatedPassage(
    DateTimeOffset PassedAt,
    TravelDirection Direction,
    VehicleClass? VehicleClass,
    double VideoOffsetSeconds,
    LightingCondition? Lighting);

/// <summary>A hand-counted reference video: its time span and all passages (ADR-0027).</summary>
public sealed record AnnotatedRecording(
    string VideoName,
    DateTimeOffset Start,
    DateTimeOffset End,
    float NominalFps,
    IReadOnlyList<AnnotatedPassage> Passages);

/// <summary>
/// Stores hand-counted recordings as an operating period of source <see cref="DataSource.Annotation"/> with its
/// passages. Importing a video again replaces its previous import, so corrected annotations can be re-imported.
/// </summary>
public static class AnnotationImport
{
    public const string AppVersion = "annotation";

    /// <returns>Id of the period that now holds the recording.</returns>
    /// <exception cref="InvalidOperationException">The counter was running during the recording.</exception>
    public static async Task<int> ReplaceAsync(
        TrafficCounterDbContext db, int cameraId, AnnotatedRecording recording, CancellationToken cancellationToken)
    {
        // Both would count the same vehicles; the counter cannot run while the camera records anyway.
        bool counterRunning = await db.OperatingPeriods.AnyAsync(
            p => p.CameraId == cameraId && p.Source == DataSource.Counter
                && p.StartedAt < recording.End && (p.EndedAt ?? p.LastHeartbeatAt) > recording.Start,
            cancellationToken);
        if (counterRunning)
        {
            throw new InvalidOperationException(
                $"The counter was running between {recording.Start:HH:mm} and {recording.End:HH:mm}; '{recording.VideoName}' would count vehicles twice.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.VehiclePassages
            .Where(p => p.Source == DataSource.Annotation && p.SourceVideo == recording.VideoName)
            .ExecuteDeleteAsync(cancellationToken);
        await db.OperatingPeriods
            .Where(p => p.Source == DataSource.Annotation && p.SourceVideo == recording.VideoName)
            .ExecuteDeleteAsync(cancellationToken);

        var period = new OperatingPeriod
        {
            // Same id for every import of this video, on every database, so a sync replaces older imports (ADR-0030).
            SyncId = DatabaseSync.AnnotationPeriodId(recording.VideoName),
            CameraId = cameraId,
            StartedAt = recording.Start,
            LastHeartbeatAt = recording.End,
            EndedAt = recording.End,
            EndReason = PeriodEndReason.Shutdown,
            NominalFps = recording.NominalFps,
            AppVersion = AppVersion,
            MachineName = Environment.MachineName,
            Source = DataSource.Annotation,
            SourceVideo = recording.VideoName,
        };
        db.OperatingPeriods.Add(period);
        await db.SaveChangesAsync(cancellationToken);

        db.VehiclePassages.AddRange(recording.Passages.Select(p => new VehiclePassage
        {
            CameraId = cameraId,
            OperatingPeriodId = period.Id,
            PassedAt = p.PassedAt,
            Direction = p.Direction,
            VehicleClass = p.VehicleClass,
            Lighting = p.Lighting,
            Source = DataSource.Annotation,
            SourceVideo = recording.VideoName,
            VideoOffsetSeconds = Math.Round(p.VideoOffsetSeconds, 3),
        }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return period.Id;
    }
}
