using TrafficCounter.Core;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.Data;

/// <summary>Where a period or passage comes from (ADR-0027). Values are persisted – never renumber.</summary>
public enum DataSource : byte
{
    /// <summary>Counted automatically by the service.</summary>
    Counter = 0,

    /// <summary>Counted by hand in a reference video (annotator ground truth).</summary>
    Annotation = 1,
}

/// <summary>A counting site (one camera with one counting line).</summary>
public sealed class Camera
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Display name of <see cref="TravelDirection.A"/>, e.g. "towards town centre".</summary>
    public string DirectionAName { get; set; } = "A";

    /// <summary>Display name of <see cref="TravelDirection.B"/>.</summary>
    public string DirectionBName { get; set; } = "B";

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A continuous period in which the counter was running. Needed to tell "no traffic" from "counter down".
/// </summary>
public sealed class OperatingPeriod
{
    /// <summary>Identity across databases, for synchronising SQL Server and PostgreSQL (ADR-0030).</summary>
    public Guid SyncId { get; set; } = Guid.NewGuid();

    public int Id { get; set; }

    public int CameraId { get; set; }

    public Camera? Camera { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset LastHeartbeatAt { get; set; }

    /// <summary>Null while running; after a crash it is set to <see cref="LastHeartbeatAt"/> at the next start.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    public PeriodEndReason? EndReason { get; set; }

    /// <summary>Configured frame rate; basis for "expected frames" when judging coverage (ADR-0024).</summary>
    public float NominalFps { get; set; }

    public required string AppVersion { get; set; }

    public required string MachineName { get; set; }

    public string? ModelName { get; set; }

    /// <summary>Hash of the effective counting configuration, to detect changes of line/ROI between periods.</summary>
    public string? ConfigHash { get; set; }

    /// <summary>Counter run or hand-counted recording; the latter covers the time of the video.</summary>
    public DataSource Source { get; set; }

    /// <summary>File name of the recording for <see cref="DataSource.Annotation"/>.</summary>
    public string? SourceVideo { get; set; }

    /// <summary>Last change of the row (set automatically); the newer copy wins when synchronising (ADR-0030).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One vehicle crossing the counting line.</summary>
public sealed class VehiclePassage
{
    /// <summary>Identity across databases, for synchronising SQL Server and PostgreSQL (ADR-0030).</summary>
    public Guid SyncId { get; set; } = Guid.NewGuid();

    public long Id { get; set; }

    public int CameraId { get; set; }

    public int OperatingPeriodId { get; set; }

    public DateTimeOffset PassedAt { get; set; }

    public TravelDirection Direction { get; set; }

    /// <summary>Null if the class was not determined (e.g. detector without class mapping).</summary>
    public VehicleClass? VehicleClass { get; set; }

    /// <summary>Share of the confidence-weighted class votes of the track that went to the winning class (0..1).</summary>
    public float? ClassConfidence { get; set; }

    public float? SpeedKmh { get; set; }

    /// <summary>Crossing position along the counting line (0 = start, 1 = end); distinguishes lanes and sidewalks.</summary>
    public float? LinePosition { get; set; }

    public int TrackId { get; set; }

    public int TrackLengthFrames { get; set; }

    /// <summary>Trailers towed by the vehicle, counted with it rather than as vehicles of their own (ADR-0013).</summary>
    public byte TrailerCount { get; set; }

    /// <summary>Counted by the service or by hand; hand-counted classes are the reference, not a model guess.</summary>
    public DataSource Source { get; set; }

    /// <summary>File name of the recording a hand-counted passage comes from.</summary>
    public string? SourceVideo { get; set; }

    /// <summary>Position in that recording, in seconds from its first frame.</summary>
    public double? VideoOffsetSeconds { get; set; }

    /// <summary>Day, dusk or night at the time of the passage (ADR-0017); null if the site has no coordinates.</summary>
    public LightingCondition? Lighting { get; set; }
}

/// <summary>Lookup table for <see cref="Core.VehicleClass"/> so that reports can join readable names.</summary>
public sealed class VehicleClassInfo
{
    public VehicleClass Id { get; set; }

    public required string Name { get; set; }
}

/// <summary>Per-minute operating statistics – tells "system off" from "running but blind" from "no traffic" (ADR-0024).</summary>
public sealed class MinuteStatistic
{
    /// <summary>Identity across databases, for synchronising SQL Server and PostgreSQL (ADR-0030).</summary>
    public Guid SyncId { get; set; } = Guid.NewGuid();

    public long Id { get; set; }

    public int CameraId { get; set; }

    public int OperatingPeriodId { get; set; }

    public DateTimeOffset MinuteStart { get; set; }

    public int FramesCaptured { get; set; }

    public int FramesProcessed { get; set; }

    public int FramesDropped { get; set; }

    public int Detections { get; set; }

    public int Passages { get; set; }

    public SourceState SourceState { get; set; }

    public QualityLevel Quality { get; set; }

    public float? Brightness { get; set; }

    public float? Contrast { get; set; }

    public float? Sharpness { get; set; }

    public LightingCondition? Lighting { get; set; }
}

/// <summary>Camera drift check against the calibration (ADR-0019).</summary>
public sealed class DriftMeasurement
{
    /// <summary>Identity across databases, for synchronising SQL Server and PostgreSQL (ADR-0030).</summary>
    public Guid SyncId { get; set; } = Guid.NewGuid();

    public long Id { get; set; }

    public int CameraId { get; set; }

    public int OperatingPeriodId { get; set; }

    public DateTimeOffset MeasuredAt { get; set; }

    /// <summary>0 unreliable, 1 ok, 2 shifted, 3 moved.</summary>
    public byte Status { get; set; }

    public float DxPixels { get; set; }

    public float DyPixels { get; set; }

    public float RotationDegrees { get; set; }

    public float Scale { get; set; }

    public int Matches { get; set; }

    public float InlierRatio { get; set; }
}

/// <summary>Significant state changes of the counter, e.g. start, stop, camera lost (ADR-0024).</summary>
public sealed class SystemEvent
{
    /// <summary>Identity across databases, for synchronising SQL Server and PostgreSQL (ADR-0030).</summary>
    public Guid SyncId { get; set; } = Guid.NewGuid();

    public long Id { get; set; }

    public int CameraId { get; set; }

    public int OperatingPeriodId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public SystemEventKind Kind { get; set; }

    public EventSeverity Severity { get; set; }

    public required string Message { get; set; }
}

/// <summary>
/// Hourly weather of the nearest DWD station (ADR-0023 no. 7), imported from Bright Sky. Not tied to a camera or
/// operating period; one row per station and hour, later imports overwrite (DWD corrects recent values).
/// </summary>
public sealed class WeatherHour
{
    public long Id { get; set; }

    /// <summary>DWD station id, e.g. "01234"; the only location reference, kept in the private configuration.</summary>
    public required string StationId { get; set; }

    /// <summary>Start of the hour the values refer to.</summary>
    public DateTimeOffset Hour { get; set; }

    public float? PrecipitationMm { get; set; }

    public float? TemperatureC { get; set; }

    public float? SunshineMinutes { get; set; }

    public float? WindSpeedKmh { get; set; }

    public float? CloudCoverPercent { get; set; }

    public float? VisibilityMeters { get; set; }

    /// <summary>Bright Sky's summary: dry, fog, rain, sleet, snow, hail, thunderstorm.</summary>
    public string? Condition { get; set; }

    public DateTimeOffset ImportedAt { get; set; }
}
