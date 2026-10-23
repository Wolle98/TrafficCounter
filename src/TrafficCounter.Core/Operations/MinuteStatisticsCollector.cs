namespace TrafficCounter.Core.Operations;

/// <summary>
/// Accumulates pipeline counters and turns them into one <see cref="MinuteStatistics"/> row per calendar minute
/// (ADR-0024). <see cref="Tick"/> is driven by the wall clock, so minutes without any frame are reported, too.
/// Not thread-safe; call from the pipeline thread.
/// </summary>
/// <param name="timeZone">
/// Zone for the stored minute start (default: local). Converting every minute keeps the offset right across
/// daylight saving changes while the service runs for weeks.
/// </param>
public sealed class MinuteStatisticsCollector(int cameraId, int operatingPeriodId, DateTimeOffset start, TimeZoneInfo? timeZone = null)
{
    private readonly TimeZoneInfo _timeZone = timeZone ?? TimeZoneInfo.Local;
    private DateTimeOffset _currentMinute = Truncate(start);
    private int _captured;
    private int _processed;
    private int _dropped;
    private int _detections;
    private int _passages;
    private double _brightnessSum;
    private double _contrastSum;
    private double _sharpnessSum;
    private int _qualitySamples;
    private SourceState _sourceState = SourceState.Unknown;
    private bool _sourceOkSeen;
    private bool _degraded;
    private bool _degradedSeen;
    private QualityLevel _quality = QualityLevel.Unknown;
    private LightingCondition? _lighting;

    public SourceState SourceState
    {
        get => _sourceState;
        set
        {
            _sourceState = value;
            _sourceOkSeen |= value == SourceState.Ok;
        }
    }

    /// <summary>Whether the source is currently degraded; a minute degraded at any time is not fully measured.</summary>
    public bool Degraded
    {
        set
        {
            _degraded = value;
            _degradedSeen |= value;
        }
    }

    /// <summary>Quality level currently determined by the quality monitor; the worst level of a minute is kept.</summary>
    public QualityLevel Quality
    {
        set => _quality = (QualityLevel)Math.Max((byte)_quality, (byte)value);
    }

    public LightingCondition? Lighting
    {
        set => _lighting = value;
    }

    public void AddCaptured(int frames = 1) => _captured += frames;

    public void AddDropped(int frames) => _dropped += frames;

    public void AddProcessed(int detections)
    {
        _processed++;
        _detections += detections;
    }

    public void AddPassages(int passages) => _passages += passages;

    public void AddQualitySample(float brightness, float contrast, float sharpness)
    {
        _brightnessSum += brightness;
        _contrastSum += contrast;
        _sharpnessSum += sharpness;
        _qualitySamples++;
    }

    /// <summary>Returns the rows of all minutes that ended before <paramref name="now"/>.</summary>
    public IReadOnlyList<MinuteStatistics> Tick(DateTimeOffset now)
    {
        List<MinuteStatistics> completed = [];
        while (now >= _currentMinute.AddMinutes(1))
        {
            completed.Add(Complete());
            _currentMinute = _currentMinute.AddMinutes(1);
        }

        return completed;
    }

    /// <summary>Returns the row of the current, partial minute – used on shutdown.</summary>
    public MinuteStatistics Flush() => Complete();

    private MinuteStatistics Complete()
    {
        // A source that was up at any time in the minute counts as Ok; one that never delivered is Unavailable. Frames
        // of a source declared Unavailable for the whole minute (a stalled camera, black frames) do not make it Ok.
        SourceState state = _sourceOkSeen || (_captured > 0 && _sourceState != SourceState.Unavailable) ? SourceState.Ok
            : _sourceState == SourceState.Unknown ? SourceState.Unavailable
            : _sourceState;
        if (state == SourceState.Ok && _degradedSeen)
        {
            state = SourceState.Degraded;
        }

        MinuteStatistics row = new(
            cameraId,
            operatingPeriodId,
            TimeZoneInfo.ConvertTime(_currentMinute, _timeZone),
            _captured,
            _processed,
            _dropped,
            _detections,
            _passages,
            state,
            _quality,
            _qualitySamples > 0 ? (float)(_brightnessSum / _qualitySamples) : null,
            _qualitySamples > 0 ? (float)(_contrastSum / _qualitySamples) : null,
            _qualitySamples > 0 ? (float)(_sharpnessSum / _qualitySamples) : null,
            _lighting);

        _captured = _processed = _dropped = _detections = _passages = _qualitySamples = 0;
        _brightnessSum = _contrastSum = _sharpnessSum = 0;
        _sourceOkSeen = _sourceState == SourceState.Ok;
        _degradedSeen = _degraded;
        _quality = QualityLevel.Unknown;
        return row;
    }

    private static DateTimeOffset Truncate(DateTimeOffset time) =>
        new(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, time.Offset);
}
