using OpenCvSharp;

namespace TrafficCounter.Vision;

/// <summary>Simple image quality indicators for camera setup and the night test (ADR-0017).</summary>
/// <param name="Brightness">Mean gray value, 0..255.</param>
/// <param name="Sharpness">Variance of the Laplacian; drops with defocus and motion blur.</param>
/// <param name="Contrast">Standard deviation of the gray values; drops with fog, a fogged window or glare.</param>
public readonly record struct ImageMetrics(double Brightness, double Sharpness, double Contrast = 0)
{
    private const int SharpnessWidth = 640;

    public static ImageMetrics Measure(Mat image)
    {
        using Mat gray = new();
        if (image.Channels() == 1)
        {
            image.CopyTo(gray);
        }
        else
        {
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        }

        Cv2.MeanStdDev(gray, out Scalar mean, out Scalar spread);

        // Measure sharpness at a fixed width so values are comparable across resolutions.
        using Mat scaled = new();
        if (gray.Width > SharpnessWidth)
        {
            var size = new Size(SharpnessWidth, (int)Math.Round(gray.Height * (double)SharpnessWidth / gray.Width));
            Cv2.Resize(gray, scaled, size, 0, 0, InterpolationFlags.Area);
        }
        else
        {
            gray.CopyTo(scaled);
        }

        using Mat laplacian = new();
        Cv2.Laplacian(scaled, laplacian, MatType.CV_64F);
        Cv2.MeanStdDev(laplacian, out Scalar _, out Scalar deviation);
        return new ImageMetrics(mean.Val0, deviation.Val0 * deviation.Val0, spread.Val0);
    }
}

/// <summary>Frame rate over a sliding window of frame timestamps.</summary>
public sealed class FrameRateMeter(int window = 30)
{
    private readonly Queue<DateTimeOffset> _timestamps = new();
    private DateTimeOffset _last;

    public double FramesPerSecond
    {
        get
        {
            if (_timestamps.Count < 2)
            {
                return 0;
            }

            double seconds = (_last - _timestamps.Peek()).TotalSeconds;
            return seconds > 0 ? (_timestamps.Count - 1) / seconds : 0;
        }
    }

    public void Add(DateTimeOffset timestamp)
    {
        _timestamps.Enqueue(timestamp);
        _last = timestamp;
        while (_timestamps.Count > window)
        {
            _timestamps.Dequeue();
        }
    }
}

public static class ExposureFormat
{
    /// <summary>
    /// UVC cameras report exposure as log2 of the exposure time in seconds (-7 ≈ 1/128 s).
    /// Returns a readable time for values in the plausible range, otherwise null (unknown unit).
    /// </summary>
    public static string? Describe(double exposure)
    {
        if (exposure is < -15 or > 3)
        {
            return null;
        }

        double seconds = Math.Pow(2, exposure);
        return seconds < 1 ? $"≈ 1/{Math.Round(1 / seconds)} s" : $"≈ {seconds:0.#} s";
    }
}
