using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace TrafficCounter.Vision;

/// <summary>A recording target: <see cref="VideoRecorder"/> (OpenCV) or <see cref="FfmpegRecorder"/> (hardware encoder).</summary>
public interface IVideoSink : IDisposable
{
    /// <summary>Name of the recording; the sidecar files are named after it.</summary>
    string Path { get; }

    /// <summary>The file the frames go to.</summary>
    string VideoPath { get; }

    long FramesWritten { get; }

    void Write(Frame frame);
}

/// <summary>
/// Pipes raw frames into ffmpeg, which encodes them to H.264 right away, e.g. with Intel Quick Sync or NVENC: the
/// graphics unit does the encoding, the CPU only copies (ADR-0032). One output frame per written frame
/// (<c>-fps_mode passthrough</c>); the real capture times are in the "*.frames.csv" sidecar as with
/// <see cref="VideoRecorder"/>. Only for the explicit recording mode (ADR-0005).
/// </summary>
public sealed class FfmpegRecorder : IVideoSink
{
    private readonly Process _process;
    private readonly Stream _input;
    private readonly TextWriter _timestamps;
    private readonly Rect? _crop;
    private readonly byte[] _buffer;
    private readonly int _rowBytes;
    private readonly Size _size;
    private long _frames;

    /// <param name="path">The .mp4 to write; also the name of the sidecar files.</param>
    /// <param name="encoder">ffmpeg encoder, e.g. h264_qsv or h264_nvenc.</param>
    public FfmpegRecorder(string ffmpeg, string path, Size frameSize, double fps, string encoder, int quality, Rect? crop = null)
    {
        _crop = crop;
        _size = crop?.Size ?? frameSize;
        _rowBytes = _size.Width * 3;
        _buffer = new byte[_rowBytes * _size.Height];
        Path = path;

        var start = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        string[] arguments =
        [
            "-hide_banner", "-loglevel", "error", "-nostats", "-y",
            "-f", "rawvideo", "-pix_fmt", "bgr24", "-s", $"{_size.Width}x{_size.Height}",
            "-framerate", fps.ToString("0.###", CultureInfo.InvariantCulture), "-i", "-",
            "-an", "-fps_mode", "passthrough", "-g", "30",
            "-pix_fmt", encoder.EndsWith("_qsv", StringComparison.Ordinal) ? "nv12" : "yuv420p",
            .. VideoCompressor.EncoderArguments(encoder, quality),
            "-movflags", "+faststart",
            path,
        ];
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        _process = Process.Start(start) ?? throw new IOException("ffmpeg could not be started.");
        // ffmpeg reports errors on stderr; read it so a full pipe never blocks it, keep the end for the log.
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { Length: > 0 } line)
            {
                LastError = line;
            }
        };
        _process.BeginErrorReadLine();
        _input = _process.StandardInput.BaseStream;
        _timestamps = FrameTimestampsFile.CreateWriter(path);
    }

    public string Path { get; }

    public string VideoPath => Path;

    public long FramesWritten => _frames;

    /// <summary>Last message ffmpeg printed (errors only).</summary>
    public string? LastError { get; private set; }

    public void Write(Frame frame)
    {
        if (_process.HasExited)
        {
            throw new IOException($"ffmpeg ended (exit code {_process.ExitCode}): {LastError}");
        }

        using Mat? cropped = _crop is Rect crop ? new Mat(frame.Image, crop) : null;
        Mat region = cropped ?? frame.Image;
        if (region.IsContinuous())
        {
            Marshal.Copy(region.Data, _buffer, 0, _buffer.Length);
        }
        else
        {
            for (int row = 0; row < _size.Height; row++)
            {
                Marshal.Copy(region.Ptr(row), _buffer, row * _rowBytes, _rowBytes);
            }
        }

        _input.Write(_buffer, 0, _buffer.Length);
        FrameTimestampsFile.Write(_timestamps, _frames++, frame.Timestamp);
    }

    public void Dispose()
    {
        try
        {
            _input.Dispose(); // end of input: ffmpeg finishes the file
            if (!_process.WaitForExit(TimeSpan.FromMinutes(2)))
            {
                _process.Kill();
            }
        }
        finally
        {
            _process.Dispose();
            _timestamps.Dispose();
        }
    }
}
