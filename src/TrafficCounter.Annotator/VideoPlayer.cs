using OpenCvSharp;
using TrafficCounter.Vision;

namespace TrafficCounter.Annotator;

/// <summary>
/// Random access to the frames of a video. Positions are frame indices; offsets come from the recording's frame
/// timestamps (<c>.frames.csv</c>) when present, because the container's frame rate of a service recording is not
/// the camera's.
/// </summary>
internal sealed class VideoPlayer : IDisposable
{
    private readonly VideoCapture _capture;
    private readonly IReadOnlyList<DateTimeOffset>? _timestamps;
    private readonly Mat _frame = new();

    private VideoPlayer(VideoCapture capture, IReadOnlyList<DateTimeOffset>? timestamps)
    {
        _capture = capture;
        _timestamps = timestamps;
        FrameCount = Math.Max(1, (int)capture.Get(VideoCaptureProperties.FrameCount));
        Fps = capture.Get(VideoCaptureProperties.Fps) is > 0 and var f ? f : 25;
    }

    public int FrameCount { get; }

    /// <summary>Frame rate of the container; used for playback timing and jumps (as before).</summary>
    public double Fps { get; }

    /// <summary>Index of the frame in <see cref="Frame"/>.</summary>
    public int Position { get; private set; }

    public Mat Frame => _frame;

    public TimeSpan Duration => OffsetOf(FrameCount - 1);

    public static VideoPlayer? Open(string path)
    {
        var capture = new VideoCapture(path, VideoCaptureAPIs.FFMPEG);
        if (!capture.IsOpened())
        {
            capture.Dispose();
            return null;
        }

        return new VideoPlayer(capture, FrameTimestampsFile.TryRead(path));
    }

    public TimeSpan OffsetOf(int frame) =>
        _timestamps is not null && frame < _timestamps.Count ? _timestamps[frame] - _timestamps[0] : TimeSpan.FromSeconds(frame / Fps);

    public TimeSpan Offset => OffsetOf(Position);

    /// <summary>Wall clock time of a frame from the recording's timestamps, or null without them.</summary>
    public DateTimeOffset? WallClockOf(int frame) => _timestamps is not null && frame < _timestamps.Count ? _timestamps[frame] : null;

    public int FrameAt(TimeSpan offset)
    {
        if (offset <= TimeSpan.Zero)
        {
            return 0;
        }

        if (_timestamps is null || _timestamps.Count == 0)
        {
            return (int)(offset.TotalSeconds * Fps);
        }

        DateTimeOffset target = _timestamps[0] + offset;
        int index = 0;
        while (index < _timestamps.Count - 1 && _timestamps[index + 1] <= target)
        {
            index++;
        }

        return index;
    }

    /// <summary>Reads the frame at <paramref name="frame"/> (clamped).</summary>
    public void Seek(int frame)
    {
        Position = Math.Clamp(frame, 0, FrameCount - 1);
        _capture.Set(VideoCaptureProperties.PosFrames, Position);
        _capture.Read(_frame);
    }

    /// <summary>Plays on by <paramref name="step"/> frames; only the last one is decoded into <see cref="Frame"/>.</summary>
    /// <returns>False at the end of the video.</returns>
    public bool Advance(int step)
    {
        int advanced = 0;
        while (advanced < step - 1 && _capture.Grab())
        {
            advanced++;
        }

        if (_capture.Read(_frame) && !_frame.Empty())
        {
            Position += advanced + 1;
            return true;
        }

        Position += advanced;
        return false;
    }

    /// <summary>
    /// The current frame scaled to at most <paramref name="maxWidth"/>, with brightness (added, -100..100) and
    /// contrast (factor) applied and the reference line drawn, as 4-channel BGRA.
    /// </summary>
    public Mat Render(int maxWidth, double brightness, double contrast, double linePosition)
    {
        Mat shown = new();
        if (_frame.Empty())
        {
            return shown;
        }

        double scale = Math.Min(1.0, (double)maxWidth / _frame.Width);
        using Mat scaled = new();
        Cv2.Resize(_frame, scaled, new Size(0, 0), scale, scale, InterpolationFlags.Area);
        if (brightness != 0 || contrast != 1)
        {
            // Contrast around the middle grey, so it does not also brighten or darken the image.
            scaled.ConvertTo(scaled, -1, contrast, brightness + (128 * (1 - contrast)));
        }

        int x = (int)(linePosition * scaled.Width);
        Cv2.Line(scaled, new Point(x, 0), new Point(x, scaled.Height), new Scalar(0, 255, 255), 2);
        Cv2.CvtColor(scaled, shown, ColorConversionCodes.BGR2BGRA);
        return shown;
    }

    public void Dispose()
    {
        _frame.Dispose();
        _capture.Dispose();
    }
}
