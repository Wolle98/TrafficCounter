using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace TrafficCounter.Vision;

/// <summary>
/// USB (UVC) camera source. A dedicated thread reads frames continuously and keeps only the newest one
/// (ADR-0011); the camera is reopened with backoff if it disappears.
/// </summary>
public sealed class UsbFrameSource(UsbCameraOptions options, ILogger<UsbFrameSource> logger, TimeProvider? timeProvider = null)
    : IFrameSource, IObservableFrameSource, IReopenableFrameSource
{
    private const int MaxConsecutiveReadFailures = 50;
    private static readonly TimeSpan[] ReconnectBackoff =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly LatestFrameBuffer _buffer = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<Action<VideoCapture>> _commands = new();
    private Thread? _thread;
    private long _sequence;
    private int _reconnects;
    private volatile bool _reopenRequested;

    public string Description => string.IsNullOrWhiteSpace(options.DeviceName)
        ? $"USB camera {options.DeviceIndex} ({options.Backend})"
        : $"USB camera '{options.DeviceName}' ({options.Backend})";

    public Action<Frame>? CaptureObserver
    {
        get => _buffer.Observer;
        set => _buffer.Observer = value;
    }

    public void Reopen() => _reopenRequested = true;

    public FrameSourceStatistics Statistics => new(_buffer.Published, _buffer.Dropped, Volatile.Read(ref _reconnects));

    /// <summary>Settings as reported by the camera after opening; null until the camera is open.</summary>
    public UsbCameraOptions? ActualSettings { get; private set; }

    public void Start()
    {
        if (_thread is not null)
        {
            throw new InvalidOperationException("Source already started.");
        }

        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "usb-capture" };
        _thread.Start();
    }

    public ValueTask<Frame> ReadAsync(CancellationToken cancellationToken) => _buffer.TakeAsync(cancellationToken);

    public bool TryRead(TimeSpan timeout, out Frame? frame) => _buffer.TryTake(timeout, out frame);

    /// <summary>
    /// Runs <paramref name="action"/> on the capture thread between two reads, so camera controls can be changed
    /// while capturing without concurrent access to the device.
    /// </summary>
    public Task<T> InvokeAsync<T>(Func<VideoCapture, T> action)
    {
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(capture =>
        {
            try
            {
                completion.SetResult(action(capture));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _thread?.Join(TimeSpan.FromSeconds(5));
        _buffer.Dispose();
        _stop.Dispose();
    }

    private void CaptureLoop()
    {
        int attempt = 0;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using VideoCapture capture = CameraSettings.Open(options);
                ActualSettings = CameraSettings.ReadBack(capture, options);
                logger.LogInformation(
                    "{Source} opened: {Width}x{Height} {FourCC} @ {Fps} fps",
                    Description, ActualSettings.Width, ActualSettings.Height, ActualSettings.FourCC, ActualSettings.Fps);
                attempt = 0;
                _reopenRequested = false;
                ReadFrames(capture);
                if (_reopenRequested)
                {
                    logger.LogWarning("{Source} closed on request, reopening", Description);
                    Interlocked.Increment(ref _reconnects);
                    continue;
                }
            }
            catch (CameraUnavailableException ex)
            {
                logger.LogWarning("{Message}", ex.Message);
            }
            catch (Exception ex) when (ex is OpenCVException or OpenCvSharpException)
            {
                logger.LogError(ex, "{Source} failed", Description);
            }

            if (_stop.IsCancellationRequested)
            {
                break;
            }

            TimeSpan delay = ReconnectBackoff[Math.Min(attempt++, ReconnectBackoff.Length - 1)];
            logger.LogInformation("Reopening {Source} in {Delay}", Description, delay);
            Interlocked.Increment(ref _reconnects);
            _stop.Token.WaitHandle.WaitOne(delay);
        }
    }

    private void ReadFrames(VideoCapture capture)
    {
        int failures = 0;
        while (!_stop.IsCancellationRequested && !_reopenRequested)
        {
            while (_commands.TryDequeue(out Action<VideoCapture>? command))
            {
                command(capture);
            }

            var image = new Mat();
            if (!capture.Read(image) || image.Empty())
            {
                image.Dispose();
                if (++failures >= MaxConsecutiveReadFailures)
                {
                    logger.LogWarning("{Source} delivered no frames, closing it", Description);
                    return;
                }

                Thread.Sleep(20);
                continue;
            }

            failures = 0;
            _buffer.Publish(new Frame(image, _time.GetLocalNow(), Interlocked.Increment(ref _sequence)));
        }
    }
}
