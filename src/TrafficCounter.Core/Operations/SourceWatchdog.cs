namespace TrafficCounter.Core.Operations;

public sealed class SourceWatchdogSettings
{
    /// <summary>Length of one judgement window; a stall is declared (or lifted) after a whole window.</summary>
    public double WindowSeconds { get; set; } = 120;

    /// <summary>Mean brightness (0–255) below which a sample counts as black, if its spread is below <see cref="BlackSpread"/>, too.</summary>
    public double BlackBrightness { get; set; } = 3;

    /// <summary>Standard deviation (0–255) below which a dark sample counts as black: a real night scene still has noise.</summary>
    public double BlackSpread { get; set; } = 2;

    /// <summary>Samples per window needed before "all black" is believed.</summary>
    public int MinSamples { get; set; } = 3;

    /// <summary>A window whose capture rate is below this share of the normal rate counts as stalled.</summary>
    public double MinRateShare { get; set; } = 0.25;

    /// <summary>Learning rate of the normal capture rate; only healthy windows are learned.</summary>
    public double RateLearning { get; set; } = 0.2;

    /// <summary>
    /// A window whose capture rate is below this share of the nominal rate counts as degraded. Only for sources with
    /// a fixed frame rate (IP cameras); a USB camera lowers its rate with longer exposure at night.
    /// </summary>
    public double MinFrameShare { get; set; } = 0.9;

    /// <summary>Healthy windows in a row needed before a degraded stream counts as measured again.</summary>
    public int DegradedHoldWindows { get; set; } = 5;
}

public enum StallReason : byte
{
    None = 0,

    /// <summary>The frames are (practically) black.</summary>
    Black = 1,

    /// <summary>Far fewer frames arrive than normally.</summary>
    FrameRate = 2,
}

/// <summary>
/// Detects a camera that still delivers frames but no usable picture: all frames black, or the frame rate collapsed
/// (seen with a USB camera that dropped to one black frame per second for hours, while the device stayed "OK").
/// Judges whole windows, so a dark night scene or a short hiccup is no stall. Not thread-safe.
/// <para>
/// Separately it tracks a degraded stream: frames keep coming, but with interruptions or (with a fixed nominal rate)
/// clearly too few, as seen with an IP camera for two hours that lost a quarter of the vehicles. Such an episode
/// lasts until <see cref="SourceWatchdogSettings.DegradedHoldWindows"/> healthy windows in a row, so the minutes in
/// between, which are just as affected, do not count as measured.
/// </para>
/// </summary>
/// <param name="nominalRate">Fixed frame rate of the source (frames per second), or null if it varies legitimately.</param>
public sealed class SourceWatchdog(DateTimeOffset start, SourceWatchdogSettings? settings = null, double? nominalRate = null)
{
    private readonly SourceWatchdogSettings _settings = settings ?? new SourceWatchdogSettings();
    private DateTimeOffset _windowStart = start;
    private long _frames;
    private int _samples;
    private int _blackSamples;
    private int _healthyWindows;

    /// <summary>Normal frames per second, learned from healthy windows; 0 until the first one.</summary>
    public double NormalRate { get; private set; }

    public StallReason Stall { get; private set; }

    public bool IsStalled => Stall != StallReason.None;

    /// <summary>True during a degraded episode: interruptions or too low a frame rate (see the class description).</summary>
    public bool IsDegraded { get; private set; }

    /// <summary>The source delivered nothing for a while and was reconnected: starts (or extends) a degraded episode.</summary>
    public void NoteInterruption()
    {
        IsDegraded = true;
        _healthyWindows = 0;
    }

    public void AddFrames(long frames) => _frames += frames;

    /// <summary>Brightness mean and standard deviation of a (downscaled) frame, 0–255.</summary>
    public void AddSample(double mean, double standardDeviation)
    {
        _samples++;
        if (mean < _settings.BlackBrightness && standardDeviation < _settings.BlackSpread)
        {
            _blackSamples++;
        }
    }

    /// <summary>Starts a new window, e.g. after the source was reopened, so the old frames are not judged again.</summary>
    public void Restart(DateTimeOffset now)
    {
        _windowStart = now;
        _frames = 0;
        _samples = 0;
        _blackSamples = 0;
    }

    /// <summary>
    /// Judges the window once it is complete and sets <see cref="Stall"/>; returns true if a window was judged (also
    /// when the state stayed the same, so a lasting stall can be acted on again).
    /// </summary>
    public bool Evaluate(DateTimeOffset now)
    {
        double seconds = (now - _windowStart).TotalSeconds;
        if (seconds < _settings.WindowSeconds)
        {
            return false;
        }

        double rate = _frames / seconds;
        StallReason reason = _samples >= _settings.MinSamples && _blackSamples == _samples ? StallReason.Black
            : NormalRate > 0 && rate < _settings.MinRateShare * NormalRate ? StallReason.FrameRate
            : StallReason.None;
        if (reason == StallReason.None && _frames > 0)
        {
            NormalRate = NormalRate == 0 ? rate : NormalRate + (_settings.RateLearning * (rate - NormalRate));
        }

        bool degraded = reason != StallReason.None
            || (nominalRate is double nominal && rate < _settings.MinFrameShare * nominal);
        if (degraded)
        {
            NoteInterruption();
        }
        else if (IsDegraded && ++_healthyWindows >= _settings.DegradedHoldWindows)
        {
            IsDegraded = false;
        }

        Restart(now);
        Stall = reason;
        return true;
    }
}
