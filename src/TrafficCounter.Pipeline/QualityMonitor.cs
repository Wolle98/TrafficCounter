using TrafficCounter.Core.Operations;

namespace TrafficCounter.Pipeline;

/// <summary>Image quality indicators of one sample (ADR-0023).</summary>
/// <param name="LineContrast">Contrast in the part of the band around the counting line.</param>
/// <param name="LineSharpness">Sharpness around the counting line – a drop or smudge right there matters most.</param>
public readonly record struct QualitySample(float Brightness, float Contrast, float Sharpness, float LineContrast, float LineSharpness);

/// <summary>Historic average per hour of day, e.g. loaded from the per-minute statistics.</summary>
public readonly record struct QualityHistory(int Hour, float Contrast, float Sharpness, int Samples);

public sealed class QualityMonitorSettings
{
    /// <summary>Below this share of the normal value the image counts as degraded.</summary>
    public float DegradedRatio { get; set; } = 0.5f;

    /// <summary>Below this share of the normal value the image counts as unusable.</summary>
    public float UnusableRatio { get; set; } = 0.2f;

    /// <summary>Samples needed per hour of day before levels are reported (otherwise Unknown).</summary>
    public int WarmupSamples { get; set; } = 20;

    /// <summary>Learning rate for the normal values; only good samples are learned.</summary>
    public float LearningRate { get; set; } = 0.02f;

    /// <summary>Consecutive samples with a new level before it is reported (avoids flicker).</summary>
    public int ConfirmSamples { get; set; } = 3;
}

/// <summary>
/// Judges image quality against a normal value per hour of day (ADR-0023), so night is not mistaken for a problem.
/// Normal values are learned only from good samples: a slowly dirtier window stays "degraded" instead of
/// becoming the new normal. Not thread-safe.
/// </summary>
public sealed class QualityMonitor(QualityMonitorSettings? settings = null)
{
    private readonly QualityMonitorSettings _settings = settings ?? new QualityMonitorSettings();
    private readonly Baseline[] _overall = CreateBaselines();
    private readonly Baseline[] _line = CreateBaselines();
    private QualityLevel _candidate = QualityLevel.Unknown;
    private int _candidateCount;

    /// <summary>Level last confirmed by <see cref="QualityMonitorSettings.ConfirmSamples"/> consecutive samples.</summary>
    public QualityLevel Current { get; private set; } = QualityLevel.Unknown;

    /// <summary>Starts from historic averages so levels are available right after a restart.</summary>
    public void Seed(IEnumerable<QualityHistory> history)
    {
        foreach (QualityHistory h in history.Where(h => h.Hour is >= 0 and < 24 && h.Samples > 0))
        {
            _overall[h.Hour] = new Baseline(h.Contrast, h.Sharpness, h.Samples);
            _line[h.Hour] = new Baseline(h.Contrast, h.Sharpness, h.Samples); // no separate history for the line region
        }
    }

    /// <summary>Evaluates a sample; returns true if the confirmed level changed.</summary>
    public bool Add(QualitySample sample, DateTimeOffset time)
    {
        int hour = time.Hour;
        QualityLevel level = Worst(
            Judge(Reference(_overall, hour), sample.Contrast, sample.Sharpness),
            Judge(Reference(_line, hour), sample.LineContrast, sample.LineSharpness));

        if (level is QualityLevel.Good or QualityLevel.Unknown)
        {
            _overall[hour] = _overall[hour].Learn(sample.Contrast, sample.Sharpness, _settings.LearningRate);
            _line[hour] = _line[hour].Learn(sample.LineContrast, sample.LineSharpness, _settings.LearningRate);
        }

        if (level == _candidate)
        {
            _candidateCount++;
        }
        else
        {
            _candidate = level;
            _candidateCount = 1;
        }

        if (_candidateCount >= _settings.ConfirmSamples && Current != _candidate)
        {
            Current = _candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Normal value for the hour; while it is still warming up, the previous hour's. Otherwise every full hour
    /// would briefly report Unknown until enough samples of the new hour were seen.
    /// </summary>
    private Baseline Reference(Baseline[] baselines, int hour)
    {
        Baseline own = baselines[hour];
        return own.Samples >= _settings.WarmupSamples ? own : baselines[(hour + 23) % 24];
    }

    private QualityLevel Judge(Baseline baseline, float contrast, float sharpness)
    {
        if (baseline.Samples < _settings.WarmupSamples || baseline.Contrast <= 0 || baseline.Sharpness <= 0)
        {
            return QualityLevel.Unknown;
        }

        float ratio = Math.Min(contrast / baseline.Contrast, sharpness / baseline.Sharpness);
        return ratio < _settings.UnusableRatio ? QualityLevel.Unusable
            : ratio < _settings.DegradedRatio ? QualityLevel.Degraded
            : QualityLevel.Good;
    }

    private static QualityLevel Worst(QualityLevel a, QualityLevel b) => (QualityLevel)Math.Max((byte)a, (byte)b);

    private static Baseline[] CreateBaselines() => new Baseline[24];

    private readonly record struct Baseline(float Contrast, float Sharpness, int Samples)
    {
        public Baseline Learn(float contrast, float sharpness, float rate)
        {
            if (Samples == 0)
            {
                return new Baseline(contrast, sharpness, 1);
            }

            // Plain average while warming up, then exponential forgetting.
            float weight = Math.Max(rate, 1f / (Samples + 1));
            return new Baseline(
                Contrast + (weight * (contrast - Contrast)),
                Sharpness + (weight * (sharpness - Sharpness)),
                Samples + 1);
        }
    }
}

/// <summary>
/// Reports a quality level only after it has differed from the last reported level for the whole hold time. A brief
/// change that returns to the reported level is never reported; levels flipping among themselves (e.g. degraded and
/// unusable on a rainy night) are reported at most once per hold time, with the level current at that moment.
/// </summary>
public sealed class SettledQualityLevel(TimeSpan hold)
{
    private DateTimeOffset? _deviatingSince;

    public QualityLevel Reported { get; private set; } = QualityLevel.Unknown;

    /// <summary>Feeds the monitor's current level; returns true if <see cref="Reported"/> changed.</summary>
    public bool Update(QualityLevel current, DateTimeOffset time)
    {
        if (current == Reported)
        {
            _deviatingSince = null;
            return false;
        }

        _deviatingSince ??= time;
        if (time - _deviatingSince < hold)
        {
            return false;
        }

        Reported = current;
        _deviatingSince = null;
        return true;
    }
}
