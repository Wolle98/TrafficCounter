using OpenCvSharp;

namespace TrafficCounter.Vision;

public enum FilePlayback
{
    /// <summary>
    /// Every frame is delivered, as fast as the consumer reads. Deterministic – used for regression tests.
    /// </summary>
    Sequential,

    /// <summary>Frames are released at their original pace; slow consumers miss frames like with a live camera.</summary>
    RealTime,
}

/// <summary>
/// Replays a recorded video (ADR-0011). Timestamps come from the "*.frames.csv" sidecar written by
/// <see cref="VideoRecorder"/>; without it they are derived from the file's frame rate and <paramref name="start"/>.
/// </summary>
public sealed class FileFrameSource : IFrameSource
{
    private readonly string _path;
    private readonly FilePlayback _playback;
    private readonly VideoCapture _capture;
    private readonly IReadOnlyList<DateTimeOffset>? _timestamps;
    private readonly DateTimeOffset _start;
    private readonly double _fps;
    private readonly LatestFrameBuffer _buffer = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _readLock = new();
    private Thread? _thread;
    private long _index;
    private bool _completed;

    public FileFrameSource(string path, FilePlayback playback, DateTimeOffset? start = null)
    {
        _path = path;
        _playback = playback;
        _capture = new VideoCapture(path, VideoCaptureAPIs.FFMPEG);
        if (!_capture.IsOpened())
        {
            _capture.Dispose();
            throw new CameraUnavailableException($"Video '{path}' could not be opened.");
        }

        _timestamps = FrameTimestampsFile.TryRead(path);
        _fps = _capture.Get(VideoCaptureProperties.Fps) is > 0 and var fps ? fps : 25;
        _start = _timestamps is { Count: > 0 } ? _timestamps[0] : start ?? File.GetLastWriteTime(path);
    }

    public string Description => $"Video file {Path.GetFileName(_path)} ({_playback})";

    public FrameSourceStatistics Statistics => new(Interlocked.Read(ref _index), _buffer.Dropped, 0);

    /// <summary>True once the end of the video has been reached and all frames were handed out.</summary>
    public bool IsCompleted => Volatile.Read(ref _completed);

    public int FrameCount => (int)_capture.Get(VideoCaptureProperties.FrameCount);

    public void Start()
    {
        if (_playback == FilePlayback.RealTime)
        {
            _thread = new Thread(PlayRealTime) { IsBackground = true, Name = "file-playback" };
            _thread.Start();
        }
    }

    /// <exception cref="EndOfStreamException">The video has ended.</exception>
    public async ValueTask<Frame> ReadAsync(CancellationToken cancellationToken)
    {
        if (_playback == FilePlayback.Sequential)
        {
            return ReadNext() ?? throw new EndOfStreamException($"End of video '{_path}'.");
        }

        while (true)
        {
            if (_buffer.TryTake(TimeSpan.Zero, out Frame? frame))
            {
                return frame;
            }

            if (IsCompleted)
            {
                throw new EndOfStreamException($"End of video '{_path}'.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(200));
            try
            {
                return await _buffer.TakeAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Re-check for the end of the video.
            }
        }
    }

    public bool TryRead(TimeSpan timeout, out Frame? frame)
    {
        if (_playback == FilePlayback.Sequential)
        {
            frame = ReadNext();
            return frame is not null;
        }

        return _buffer.TryTake(timeout, out frame);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _thread?.Join(TimeSpan.FromSeconds(5));
        _buffer.Dispose();
        _capture.Dispose();
        _stop.Dispose();
    }

    private Frame? ReadNext()
    {
        lock (_readLock)
        {
            var image = new Mat();
            if (!_capture.Read(image) || image.Empty())
            {
                image.Dispose();
                Volatile.Write(ref _completed, true);
                return null;
            }

            long index = Interlocked.Increment(ref _index) - 1;
            return new Frame(image, TimestampOf(index), index + 1);
        }
    }

    private DateTimeOffset TimestampOf(long index) =>
        _timestamps is not null && index < _timestamps.Count
            ? _timestamps[(int)index]
            : _start + TimeSpan.FromSeconds(index / _fps);

    private void PlayRealTime()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!_stop.IsCancellationRequested)
        {
            Frame? frame = ReadNext();
            if (frame is null)
            {
                return;
            }

            TimeSpan due = frame.Timestamp - _start;
            TimeSpan wait = due - clock.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                _stop.Token.WaitHandle.WaitOne(wait);
            }

            _buffer.Publish(frame);
        }
    }
}
