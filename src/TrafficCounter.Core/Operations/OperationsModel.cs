using System.Text.Json.Serialization;

namespace TrafficCounter.Core.Operations;

// Enum values are persisted – never renumber.

public enum SourceState : byte
{
    Unknown = 0,
    Ok = 1,
    Unavailable = 2,

    /// <summary>Frames arrived, but the stream was interrupted or too slow: not fully measured (ADR-0024).</summary>
    Degraded = 3,
}

/// <summary>Image quality level (ADR-0023).</summary>
public enum QualityLevel : byte
{
    Unknown = 0,
    Good = 1,
    Degraded = 2,
    Unusable = 3,
}

public enum LightingCondition : byte
{
    Unknown = 0,
    Day = 1,
    Dusk = 2,
    Night = 3,
}

public enum PeriodEndReason : byte
{
    /// <summary>Orderly shutdown.</summary>
    Shutdown = 1,

    /// <summary>Detected at the next start: the period was never closed (crash, power loss).</summary>
    Unclean = 2,

    /// <summary>Stopped because of a fatal error.</summary>
    Error = 3,
}

public enum SystemEventKind : byte
{
    Started = 1,
    Stopped = 2,
    UncleanShutdownDetected = 3,
    SourceConnected = 4,
    SourceLost = 5,
    QualityChanged = 6,
    ConfigurationChanged = 7,
    StorageUnavailable = 8,
    StorageRecovered = 9,
    Error = 10,
    CameraMoved = 11,
    SourceDegraded = 12,
    ViewObstructed = 13,
}

public enum EventSeverity : byte
{
    Information = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>Camera identity and the display names of its two directions.</summary>
public sealed record CameraRegistration(string Name, string DirectionAName, string DirectionBName);

/// <summary>Information stored when a counting run starts (ADR-0024).</summary>
public sealed record OperatingPeriodStart(
    string CameraName,
    DateTimeOffset StartedAt,
    string AppVersion,
    string MachineName,
    double NominalFps,
    string? ModelName,
    string? ConfigHash);

/// <summary>Anything the counter persists. Serialisable for the offline fallback file.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PassageRecord), "passage")]
[JsonDerivedType(typeof(MinuteStatistics), "minute")]
[JsonDerivedType(typeof(SystemEventRecord), "event")]
[JsonDerivedType(typeof(DriftRecord), "drift")]
public abstract record StoreRecord(int CameraId, int OperatingPeriodId);

public sealed record PassageRecord(int CameraId, int OperatingPeriodId, VehiclePassageEvent Passage, LightingCondition? Lighting = null)
    : StoreRecord(CameraId, OperatingPeriodId);

/// <summary>One row per minute while the service runs – also serves as heartbeat (ADR-0024).</summary>
public sealed record MinuteStatistics(
    int CameraId,
    int OperatingPeriodId,
    DateTimeOffset MinuteStart,
    int FramesCaptured,
    int FramesProcessed,
    int FramesDropped,
    int Detections,
    int Passages,
    SourceState SourceState,
    QualityLevel Quality,
    float? Brightness,
    float? Contrast,
    float? Sharpness,
    LightingCondition? Lighting)
    : StoreRecord(CameraId, OperatingPeriodId);

public sealed record SystemEventRecord(
    int CameraId,
    int OperatingPeriodId,
    DateTimeOffset OccurredAt,
    SystemEventKind Kind,
    EventSeverity Severity,
    string Message)
    : StoreRecord(CameraId, OperatingPeriodId);

/// <summary>Result of a camera drift check against the calibration (ADR-0019).</summary>
/// <param name="Status">0 unreliable, 1 ok, 2 shifted, 3 moved.</param>
public sealed record DriftRecord(
    int CameraId,
    int OperatingPeriodId,
    DateTimeOffset MeasuredAt,
    byte Status,
    float DxPixels,
    float DyPixels,
    float RotationDegrees,
    float Scale,
    int Matches,
    float InlierRatio)
    : StoreRecord(CameraId, OperatingPeriodId);

/// <summary>Persistence port for the counter (implemented with EF Core in TrafficCounter.Data).</summary>
public interface ITrafficStore
{
    /// <summary>Returns the camera's id, creating it on first use and updating its direction names.</summary>
    Task<int> EnsureCameraAsync(CameraRegistration camera, CancellationToken cancellationToken);

    /// <summary>
    /// Closes periods of the camera that were never ended (as <see cref="PeriodEndReason.Unclean"/> at their last
    /// heartbeat) and opens a new one. Returns the new period's id and the number of periods closed.
    /// </summary>
    Task<(int PeriodId, int UncleanPeriodsClosed)> StartPeriodAsync(int cameraId, OperatingPeriodStart start, CancellationToken cancellationToken);

    Task EndPeriodAsync(int periodId, DateTimeOffset endedAt, PeriodEndReason reason, CancellationToken cancellationToken);

    /// <summary>
    /// Average contrast and sharpness per hour of day over minutes since <paramref name="since"/> that were judged good
    /// or not judged yet – the starting point for the quality monitor after a restart (ADR-0023).
    /// </summary>
    Task<IReadOnlyList<(int Hour, float Contrast, float Sharpness, int Samples)>> GetQualityHistoryAsync(
        int cameraId, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Writes a batch atomically; minute rows advance the period's heartbeat.</summary>
    Task WriteAsync(IReadOnlyList<StoreRecord> records, CancellationToken cancellationToken);
}
