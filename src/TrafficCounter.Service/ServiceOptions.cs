using Serilog.Events;
using TrafficCounter.Core.Operations;
using TrafficCounter.Detection.Onnx;
using TrafficCounter.Pipeline;
using TrafficCounter.Tracking;
using TrafficCounter.Vision;

namespace TrafficCounter.Service;

public enum SourceType
{
    Usb,
    File,
    Rtsp,
}

public sealed class SourceOptions
{
    public SourceType Type { get; set; } = SourceType.Usb;

    /// <summary>
    /// Short name of the camera (e.g. "NC400"), put in front of every event message, so events of two cameras counting
    /// the same street can be told apart. Default: the USB device name, else the source description.
    /// </summary>
    public string? Name { get; set; }

    public UsbCameraOptions Usb { get; set; } = new();

    public FileSourceOptions File { get; set; } = new();

    /// <summary>IP camera; set the password via user secrets or TrafficCounter__Source__Rtsp__Password.</summary>
    public RtspCameraOptions Rtsp { get; set; } = new();
}

public sealed class FileSourceOptions
{
    public string? Path { get; set; }

    public FilePlayback Playback { get; set; } = FilePlayback.RealTime;
}

public sealed class StorageOptions
{
    /// <summary>
    /// SQL Server (default) or PostgreSQL, e.g. on the NAS (ADR-0030). The connection string "TrafficCounter" must
    /// match; keep the PostgreSQL password in user secrets or an environment variable.
    /// </summary>
    public Data.DatabaseProvider Provider { get; set; } = Data.DatabaseProvider.SqlServer;

    /// <summary>JSONL file used while the database is unreachable (ADR-0010). Default: next to the logs.</summary>
    public string? FallbackPath { get; set; }

    /// <summary>Apply pending EF Core migrations at start.</summary>
    public bool ApplyMigrations { get; set; } = true;
}

public sealed class LoggingOptions
{
    /// <summary>Log directory. Default: %ProgramData%\TrafficCounter\logs as a service, "logs" next to the exe otherwise.</summary>
    public string? Directory { get; set; }

    public LogEventLevel FileLevel { get; set; } = LogEventLevel.Debug;

    public LogEventLevel ConsoleLevel { get; set; } = LogEventLevel.Information;

    public int RetainedDays { get; set; } = 14;
}

public sealed class DriftOptions
{
    /// <summary>Reference created by the calibrator. Default: site.drift.json next to the TRAFFICCOUNTER_CONFIG file.</summary>
    public string? ReferencePath { get; set; }

    public int IntervalMinutes { get; set; } = 5;

    public DriftSettings Thresholds { get; set; } = new();
}

/// <summary>Root of the "TrafficCounter" configuration section.</summary>
public sealed class ServiceOptions
{
    public const string SectionName = "TrafficCounter";

    public SourceOptions Source { get; set; } = new();

    public YoloDetectorOptions Detector { get; set; } = new();

    public TrackerSettings Tracker { get; set; } = new();

    /// <summary>Processing rate and motion gate, to reduce the load (ADR-0029).</summary>
    public ProcessingOptions Processing { get; set; } = new();

    public SiteOptions Site { get; set; } = new();

    public StorageOptions Storage { get; set; } = new();

    public LoggingOptions Logging { get; set; } = new();

    public DriftOptions Drift { get; set; } = new();

    /// <summary>Reference recordings by the service (ADR-0005 no. 3); off unless enabled explicitly.</summary>
    public RecordingOptions Recording { get; set; } = new();

    /// <summary>Collection of anonymised training samples (ADR-0026); off unless an end date is set.</summary>
    public SampleCollectionOptions SampleCollection { get; set; } = new();

    /// <summary>Hourly weather import from the DWD (ADR-0023); off unless a station is set.</summary>
    public WeatherOptions Weather { get; set; } = new();

    /// <summary>Daily import of public and school holidays (ADR-0051); off unless a region is set.</summary>
    public HolidayOptions Holidays { get; set; } = new();

    /// <summary>Show the debug window (console mode only, ADR-0012).</summary>
    public bool Preview { get; set; }

    /// <summary>Compute brightness, contrast and sharpness of the band every n processed frames (ADR-0023).</summary>
    public int QualitySampleInterval { get; set; } = 25;

    /// <summary>
    /// A new image quality level is logged and stored as an event only after it has held this long, so dark or rainy
    /// nights that flip between levels within seconds do not flood the events. The per-minute statistics still
    /// carry the immediate level.
    /// </summary>
    public double QualityEventHoldSeconds { get; set; } = 180;

    /// <summary>Detection of a camera that delivers only black frames or far too few (stalled); it is then reopened.</summary>
    public SourceWatchdogSettings SourceWatchdog { get; set; } = new();

    /// <summary>Detection of raindrops on the window pane at night (ADR-0024); their minutes count as not fully measured.</summary>
    public PaneMonitorSettings PaneMonitor { get; set; } = new();

    /// <summary>Round trip to the router per minute in the log, to tell WLAN problems from camera problems (IP cameras only).</summary>
    public NetworkProbeOptions NetworkProbe { get; set; } = new();

    /// <summary>Day/night scene times of the IP camera following sunrise and sunset (ADR-0042); off by default.</summary>
    public CameraScenesOptions CameraScenes { get; set; } = new();
}
