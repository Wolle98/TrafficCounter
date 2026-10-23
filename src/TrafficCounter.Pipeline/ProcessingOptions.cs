using TrafficCounter.Vision;

namespace TrafficCounter.Pipeline;

/// <summary>
/// Settings that trade computing load against detection rate (ADR-0029), so the counter also runs on a notebook
/// with integrated graphics. Defaults keep the full rate and are therefore the most accurate.
/// </summary>
public sealed class ProcessingOptions
{
    /// <summary>
    /// Highest number of frames per second that go through detection, tracking and counting; 0 processes every
    /// frame. Frames above the rate are skipped entirely. 10–15 is plenty for counting (a car is 1–3 s in view).
    /// </summary>
    public double MaxFps { get; set; }

    /// <summary>Runs the detector only while something moves in the band.</summary>
    public MotionGateOptions MotionGate { get; set; } = new();

    /// <summary>Optional enhancement of dark images before detection; off by default.</summary>
    public EnhancementOptions Enhancement { get; set; } = new();
}
