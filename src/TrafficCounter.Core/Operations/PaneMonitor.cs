namespace TrafficCounter.Core.Operations;

public sealed class PaneMonitorSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Seconds between two measurements of a frame.</summary>
    public double SampleSeconds { get; set; } = 10;

    /// <summary>Length of one judgement window.</summary>
    public double WindowSeconds { get; set; } = 120;

    /// <summary>
    /// Median number of drop-like discs per frame from which a window counts as "drops on the pane". A dry night
    /// gives about 10-20 (lamps, lit windows, road markings), a rainy one 60 and more.
    /// </summary>
    public int MinDiscs { get; set; } = 35;

    /// <summary>Clear windows in a row needed before the view counts as clear again.</summary>
    public int ClearWindows { get; set; } = 5;

    /// <summary>
    /// Disc scale for the detector (Gaussian sigma at 640 px width) and the subtracted background. The defaults fit an
    /// f/1.0 camera; the blur disc of a drop grows with the aperture, so a camera with a smaller aperture needs smaller
    /// values, determined on a rainy night with that camera.
    /// </summary>
    public double DiscSigma { get; set; } = 3.5;

    public double BackgroundSigma { get; set; } = 10;

    /// <summary>Disc area range in pixels at 640 px width.</summary>
    public float MinDiscArea { get; set; } = 20;

    public float MaxDiscArea { get; set; } = 400;
}

/// <summary>
/// Decides from the drop counts of single frames whether raindrops on the window pane impair the view (ADR-0024).
/// Only judged in the dark: by day the drops are hardly visible and have not reduced the counts. An episode starts
/// with one window whose median reaches <see cref="PaneMonitorSettings.MinDiscs"/> and ends after
/// <see cref="PaneMonitorSettings.ClearWindows"/> clear windows in a row, like a degraded stream. Not thread-safe.
/// </summary>
public sealed class PaneMonitor(DateTimeOffset start, PaneMonitorSettings? settings = null)
{
    private readonly PaneMonitorSettings _settings = settings ?? new PaneMonitorSettings();
    private readonly List<int> _samples = [];
    private DateTimeOffset _windowStart = start;
    private DateTimeOffset _nextSample = start;
    private int _clearWindows;

    /// <summary>True while drops on the pane impair the view.</summary>
    public bool HasDrops { get; private set; }

    /// <summary>Median of the last judged window (for the log).</summary>
    public int LastMedian { get; private set; }

    /// <summary>True when the next frame should be measured.</summary>
    public bool IsSampleDue(DateTimeOffset now) => _settings.Enabled && now >= _nextSample;

    public void AddSample(int discs, DateTimeOffset now)
    {
        _samples.Add(discs);
        _nextSample = now.AddSeconds(_settings.SampleSeconds);
    }

    /// <summary>
    /// Judges the window once it is complete. In daylight no samples are taken and a running episode ends: the counts
    /// agree by day even with drops. Returns true if a window was judged.
    /// </summary>
    public bool Evaluate(DateTimeOffset now, LightingCondition lighting)
    {
        if (!_settings.Enabled || (now - _windowStart).TotalSeconds < _settings.WindowSeconds)
        {
            return false;
        }

        bool dark = lighting is LightingCondition.Night or LightingCondition.Dusk;
        if (dark && _samples.Count > 0)
        {
            List<int> sorted = [.. _samples.Order()];
            LastMedian = sorted[sorted.Count / 2];
            if (LastMedian >= _settings.MinDiscs)
            {
                HasDrops = true;
                _clearWindows = 0;
            }
            else if (HasDrops && ++_clearWindows >= _settings.ClearWindows)
            {
                HasDrops = false;
            }
        }
        else if (!dark)
        {
            HasDrops = false;
            _clearWindows = 0;
        }

        _samples.Clear();
        _windowStart = now;
        return true;
    }
}
