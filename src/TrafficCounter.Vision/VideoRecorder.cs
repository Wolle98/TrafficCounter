using OpenCvSharp;

namespace TrafficCounter.Vision;

/// <summary>
/// Writes frames to a video file plus the "*.frames.csv" timestamp sidecar: MPEG-4 by default, or Motion JPEG for
/// recordings that are converted to H.264 afterwards (much less CPU per frame, so weak machines keep up).
/// Only for the explicit recording mode (reference videos, ADR-0005); never used in normal operation.
/// </summary>
public sealed class VideoRecorder : IVideoSink
{
    private readonly VideoWriter _writer;
    private readonly TextWriter _timestamps;
    private readonly Rect? _crop;
    private long _frames;

    /// <param name="path">Name of the recording; the sidecar files are named after it.</param>
    /// <param name="crop">Optional region to store instead of the full frame, e.g. the evaluation band.</param>
    /// <param name="motionJpeg">
    /// Write Motion JPEG into "&lt;path without extension&gt;.avi" instead of MPEG-4 into <paramref name="path"/>;
    /// the caller converts it into <paramref name="path"/> later (<see cref="VideoPath"/>).
    /// </param>
    public VideoRecorder(string path, Size frameSize, double fps, Rect? crop = null, bool motionJpeg = false)
    {
        _crop = crop;
        Size size = crop?.Size ?? frameSize;
        VideoPath = motionJpeg ? System.IO.Path.ChangeExtension(path, ".avi") : path;
        _writer = new VideoWriter(VideoPath, motionJpeg ? FourCC.MJPG : FourCC.MP4V, fps, size);
        if (!_writer.IsOpened())
        {
            _writer.Dispose();
            throw new IOException($"Video file '{VideoPath}' could not be created.");
        }

        if (motionJpeg)
        {
            // Visually lossless for the later H.264 conversion, about half the size of the default 95.
            _writer.Set(VideoWriterProperties.Quality, 90);
        }

        _timestamps = FrameTimestampsFile.CreateWriter(path);
        Path = path;
    }

    /// <summary>Name of the recording (sidecar files).</summary>
    public string Path { get; }

    /// <summary>The file the frames are written to; differs from <see cref="Path"/> for Motion JPEG.</summary>
    public string VideoPath { get; }

    public long FramesWritten => _frames;

    public void Write(Frame frame)
    {
        if (_crop is Rect crop)
        {
            using Mat region = new(frame.Image, crop);
            _writer.Write(region);
        }
        else
        {
            _writer.Write(frame.Image);
        }

        FrameTimestampsFile.Write(_timestamps, _frames++, frame.Timestamp);
    }

    public void Dispose()
    {
        _writer.Dispose();
        _timestamps.Dispose();
    }
}
