using OpenCvSharp;

namespace TrafficCounter.Vision;

/// <summary>Settings of <see cref="MotionGate"/> (ADR-0029).</summary>
public sealed class MotionGateOptions
{
    /// <summary>Off by default: the detector runs on every processed frame.</summary>
    public bool Enabled { get; set; }

    /// <summary>Width the band is scaled to for the comparison; small is cheap and ignores sensor noise.</summary>
    public int AnalysisWidth { get; set; } = 320;

    /// <summary>Grey value change (0–255) from which a pixel counts as changed.</summary>
    public int PixelThreshold { get; set; } = 25;

    /// <summary>Share of changed pixels in the band (outside the ignore zones) that counts as motion.</summary>
    public double MinChangedFraction { get; set; } = 0.002;

    /// <summary>After motion the detector keeps running this long, so a vehicle slowing down is not lost.</summary>
    public double HoldSeconds { get; set; } = 1.5;

    /// <summary>The detector runs at least this often even without motion, as a safety net.</summary>
    public double MaxSkipSeconds { get; set; } = 5;
}

/// <summary>
/// Decides per frame whether the detector has to run: only if something moves in the evaluation band, shortly
/// after that, or at least every few seconds (ADR-0029). Compares each frame with the previous one on a small
/// blurred greyscale copy of the band; parked vehicles and empty night streets cost almost nothing. Not thread-safe.
/// </summary>
public sealed class MotionGate(MotionGateOptions options) : IDisposable
{
    private Mat? _previous;
    private Mat? _mask;
    private DateTimeOffset _lastMotion = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRun = DateTimeOffset.MinValue;

    /// <summary>Share of changed pixels in the last analysed frame (for diagnostics).</summary>
    public double LastChangedFraction { get; private set; }

    /// <param name="band">The evaluation band of the frame.</param>
    /// <param name="ignore">Regions of the band (in band pixels) where changes do not count, e.g. ignore zones.</param>
    public bool ShouldDetect(Mat band, DateTimeOffset timestamp, IReadOnlyList<Rect> ignore)
    {
        if (!options.Enabled)
        {
            return true;
        }

        double scale = Math.Min(1.0, (double)options.AnalysisWidth / band.Width);
        using Mat small = new();
        Cv2.Resize(band, small, new Size(0, 0), scale, scale, InterpolationFlags.Area);
        using Mat grey = small.Channels() == 1 ? small.Clone() : small.CvtColor(ColorConversionCodes.BGR2GRAY);
        Mat blurred = new();
        Cv2.GaussianBlur(grey, blurred, new Size(5, 5), 0);

        bool motion = false;
        bool first = _previous is null || _previous.Size() != blurred.Size();
        if (_previous is not null && !first)
        {
            _mask ??= CreateMask(blurred.Size(), ignore, scale);
            using Mat difference = new();
            Cv2.Absdiff(blurred, _previous, difference);
            using Mat changed = difference.Threshold(options.PixelThreshold, 255, ThresholdTypes.Binary);
            Cv2.BitwiseAnd(changed, _mask, changed);
            int area = Cv2.CountNonZero(_mask);
            LastChangedFraction = area == 0 ? 0 : (double)Cv2.CountNonZero(changed) / area;
            motion = LastChangedFraction >= options.MinChangedFraction;
        }
        _previous?.Dispose();
        _previous = blurred;

        if (motion)
        {
            _lastMotion = timestamp;
        }

        // The first frame (or the first after a size change) has nothing to compare with: detect once.
        bool run = first
            || motion
            || (timestamp - _lastMotion).TotalSeconds < options.HoldSeconds
            || (timestamp - _lastRun).TotalSeconds >= options.MaxSkipSeconds;
        if (run)
        {
            _lastRun = timestamp;
        }

        return run;
    }

    /// <summary>Forgets the previous frame, e.g. after a change of the frame size.</summary>
    public void Reset()
    {
        _previous?.Dispose();
        _previous = null;
        _mask?.Dispose();
        _mask = null;
    }

    public void Dispose() => Reset();

    private static Mat CreateMask(Size size, IReadOnlyList<Rect> ignore, double scale)
    {
        var mask = new Mat(size, MatType.CV_8UC1, Scalar.All(255));
        foreach (Rect zone in ignore)
        {
            var scaled = new Rect(
                (int)(zone.X * scale), (int)(zone.Y * scale), (int)Math.Ceiling(zone.Width * scale), (int)Math.Ceiling(zone.Height * scale));
            Cv2.Rectangle(mask, scaled, Scalar.All(0), -1);
        }

        return mask;
    }
}
