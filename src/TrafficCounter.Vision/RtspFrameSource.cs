using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace TrafficCounter.Vision;

/// <summary>Settings for an IP camera stream (ADR-0011, ADR-0017).</summary>
public sealed class RtspCameraOptions
{
    /// <summary>Stream URL without credentials, e.g. rtsp://192.168.1.20:554/h264Preview_01_main.</summary>
    public string Url { get; set; } = "";

    public string? UserName { get; set; }

    /// <summary>Never store in a repository – use user secrets or an environment variable (ADR-0002).</summary>
    public string? Password { get; set; }

    /// <summary>TCP avoids the smeared frames of lost UDP packets.</summary>
    public bool UseTcp { get; set; } = true;

    /// <summary>Nominal frame rate of the stream, used for coverage calculations.</summary>
    public double Fps { get; set; } = 25;

    /// <summary>URL including credentials, for opening the stream only.</summary>
    public string ConnectionUrl()
    {
        if (string.IsNullOrEmpty(UserName) || !Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri))
        {
            return Url;
        }

        var builder = new UriBuilder(uri) { UserName = Uri.EscapeDataString(UserName), Password = Uri.EscapeDataString(Password ?? "") };
        return builder.Uri.AbsoluteUri;
    }

    /// <summary>Environment variable with the stream password, the same the service reads (TrafficCounter:Source:Rtsp:Password).</summary>
    public const string PasswordVariable = "TrafficCounter__Source__Rtsp__Password";

    /// <summary>
    /// Options for the tools (calibrator, camera probe) from <c>--rtsp</c> and <c>--user</c>. The password comes only
    /// from <see cref="PasswordVariable"/> (also a stored user variable), never from the command line, where it would
    /// end up in the shell history. A URL with credentials is refused for the same reason.
    /// </summary>
    public static RtspCameraOptions ForTool(string url, string? userName, double? fps = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("rtsp" or "rtsps"))
        {
            throw new ArgumentException($"Not an RTSP URL: {url}");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException($"Put no credentials into the URL; pass --user and set the password in {PasswordVariable}.");
        }

        var options = new RtspCameraOptions { Url = url, UserName = userName };
        if (!string.IsNullOrEmpty(userName))
        {
            options.Password = Core.PrivateLocations.Get(PasswordVariable)
                ?? throw new ArgumentException($"Set the password of '{userName}' in the environment variable {PasswordVariable}.");
        }

        if (fps is double f)
        {
            options.Fps = f;
        }

        return options;
    }

    /// <summary>URL safe for logs: credentials removed.</summary>
    public string DisplayUrl() =>
        Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri) ? new UriBuilder(uri) { UserName = "", Password = "" }.Uri.AbsoluteUri : "(invalid URL)";
}

/// <summary>
/// IP camera via RTSP (FFmpeg backend). Like <see cref="UsbFrameSource"/> a capture thread keeps only the newest
/// frame, and the stream is reopened with backoff when it breaks off.
/// </summary>
public sealed class RtspFrameSource(RtspCameraOptions options, ILogger<RtspFrameSource> logger, TimeProvider? timeProvider = null)
    : IFrameSource, IObservableFrameSource, IReopenableFrameSource
{
    private const int MaxConsecutiveReadFailures = 50;
    private static readonly TimeSpan[] ReconnectBackoff =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    private static readonly Lock EnvironmentLock = new();

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly LatestFrameBuffer _buffer = new();
    private readonly CancellationTokenSource _stop = new();
    private Thread? _thread;
    private long _sequence;
    private int _reconnects;
    private volatile bool _reopenRequested;

    public string Description => $"RTSP camera {options.DisplayUrl()}";

    public Action<Frame>? CaptureObserver
    {
        get => _buffer.Observer;
        set => _buffer.Observer = value;
    }

    public void Reopen() => _reopenRequested = true;

    public FrameSourceStatistics Statistics => new(_buffer.Published, _buffer.Dropped, Volatile.Read(ref _reconnects));

    public void Start()
    {
        if (_thread is not null)
        {
            throw new InvalidOperationException("Source already started.");
        }

        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "rtsp-capture" };
        _thread.Start();
    }

    public ValueTask<Frame> ReadAsync(CancellationToken cancellationToken) => _buffer.TakeAsync(cancellationToken);

    public bool TryRead(TimeSpan timeout, out Frame? frame) => _buffer.TryTake(timeout, out frame);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _thread?.Join(TimeSpan.FromSeconds(10));
        _buffer.Dispose();
        _stop.Dispose();
    }

    private VideoCapture Open()
    {
        // OpenCV reads FFmpeg options from this environment variable when opening a stream.
        lock (EnvironmentLock)
        {
            Environment.SetEnvironmentVariable(
                "OPENCV_FFMPEG_CAPTURE_OPTIONS",
                options.UseTcp ? "rtsp_transport;tcp|stimeout;5000000" : "stimeout;5000000");
            var capture = new VideoCapture(options.ConnectionUrl(), VideoCaptureAPIs.FFMPEG);
            if (!capture.IsOpened())
            {
                capture.Dispose();
                throw new CameraUnavailableException($"{Description} could not be opened.");
            }

            capture.Set(VideoCaptureProperties.BufferSize, 1);
            return capture;
        }
    }

    private void CaptureLoop()
    {
        int attempt = 0;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using VideoCapture capture = Open();
                logger.LogInformation(
                    "{Source} opened: {Width}x{Height} @ {Fps} fps",
                    Description,
                    capture.Get(VideoCaptureProperties.FrameWidth),
                    capture.Get(VideoCaptureProperties.FrameHeight),
                    capture.Get(VideoCaptureProperties.Fps));
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
