using System.Collections.Concurrent;
using System.Text.Json;
using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Vision;

namespace TrafficCounter.Service;

/// <summary>Reference recordings made by the running service (ADR-0005 no. 3, ADR-0032).</summary>
public sealed class RecordingOptions
{
    /// <summary>Must be switched on explicitly; without it, the service never writes images.</summary>
    public bool Enabled { get; set; }

    /// <summary>Target folder (private, never the public repository). Default: %ProgramData%\TrafficCounter\recordings.</summary>
    public string? Directory { get; set; }

    /// <summary>Optional band (share of the image height from the top) to store instead of the full frame.</summary>
    public double? BandTop { get; set; }

    public double? BandBottom { get; set; }

    /// <summary>ffmpeg for the H.264 conversion afterwards; empty: PATH, then the WinGet links folder.</summary>
    public string? FfmpegPath { get; set; }

    /// <summary>libx264 (CPU), h264_qsv (Intel Quick Sync) or h264_nvenc; "none" keeps MPEG-4.</summary>
    public string Encoder { get; set; } = "h264_qsv";

    public int Quality { get; set; } = VideoCompressor.DefaultQuality;
}

/// <summary>One entry of "schedule.json" in the recording folder.</summary>
public sealed record RecordingSlot(DateTime Start, double Minutes, string[]? Tags = null)
{
    public DateTime End => Start.AddMinutes(Math.Min(Minutes, RecordingPolicy.MaxDuration.TotalMinutes));
}

/// <summary>
/// Records reference videos from the frames the service captures anyway, so counting continues while recording
/// and its result can be compared with a hand count of the same video. The slots come from "schedule.json" in the
/// recording folder, which is re-read every 30 s: a recording can be planned without restarting the service.
/// Writing runs on its own thread with a bounded queue; when it cannot keep up, frames are dropped from the video,
/// never from counting. Afterwards the video is converted to H.264 in a low-priority ffmpeg process (ADR-0031).
/// </summary>
internal sealed class ScheduledRecorder : IDisposable
{
    public const string ScheduleFileName = "schedule.json";
    private const int QueueCapacity = 90;
    private static readonly TimeSpan ScheduleRefresh = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    private readonly RecordingOptions _options;
    private readonly double _fps;
    private readonly string? _siteConfigPath;
    private readonly ILogger _logger;
    private readonly string? _ffmpeg;
    private readonly HashSet<DateTime> _done = [];
    private IReadOnlyList<RecordingSlot> _slots = [];
    private DateTimeOffset _nextRefresh = DateTimeOffset.MinValue;
    private readonly Lock _lock = new();
    private Session? _session;
    private bool _disposed;

    public ScheduledRecorder(RecordingOptions options, double fps, string? siteConfigPath, string defaultDirectory, ILogger logger)
    {
        _options = options;
        _fps = fps > 0 ? fps : 30;
        _siteConfigPath = siteConfigPath;
        _logger = logger;
        Directory = Path.GetFullPath(options.Directory ?? defaultDirectory);
        _ffmpeg = options.Encoder == "none" ? null : VideoCompressor.FindFfmpeg(string.IsNullOrEmpty(options.FfmpegPath) ? null : options.FfmpegPath);
        RecordingPolicy.EnsureNotInPublicRepository(Path.Combine(Directory, "x.mp4"));
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get; }

    /// <summary>Called for every captured frame (before the processing rate is reduced).</summary>
    /// <summary>
    /// Records the frame if a slot is running. Called on the camera's capture thread for every frame when the source
    /// supports it (<see cref="IObservableFrameSource"/>), so stalls of the counting do not leave gaps in the video.
    /// </summary>
    public void Offer(Frame frame, object? cameraSettings)
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                OfferLocked(frame, cameraSettings);
            }
        }
    }

    private void OfferLocked(Frame frame, object? cameraSettings)
    {
        DateTimeOffset now = frame.Timestamp;
        if (now >= _nextRefresh)
        {
            _nextRefresh = now + ScheduleRefresh;
            _slots = ReadSchedule();
        }

        DateTime local = now.LocalDateTime;
        if (_session is { } running && local >= running.Slot.End)
        {
            Stop("end of the slot");
        }

        if (_session is null)
        {
            RecordingSlot? slot = _slots.FirstOrDefault(s => local >= s.Start && local < s.End && !_done.Contains(s.Start));
            if (slot is null)
            {
                return;
            }

            _done.Add(slot.Start);
            _session = Start(slot, frame, cameraSettings);
        }

        _session?.Enqueue(frame);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
        }

        Stop("service stopping", wait: TimeSpan.FromSeconds(30));
    }

    private Session Start(RecordingSlot slot, Frame first, object? cameraSettings)
    {
        string path = Path.Combine(Directory, $"{first.Timestamp.LocalDateTime:yyyyMMdd-HHmmss}.mp4");
        Rect? crop = _options.BandTop is double top && _options.BandBottom is double bottom
            ? RecordingPolicy.BandRect(first.Image.Size(), top, bottom)
            : null;
        Size size = first.Image.Size();
        string? ffmpeg = _ffmpeg;
        RecordingOptions options = _options;
        double fps = _fps;

        // With a hardware encoder, ffmpeg gets the frames directly and writes H.264 (the graphics unit encodes).
        // Otherwise Motion JPEG (cheap) converted afterwards, or MPEG-4 without ffmpeg; MPEG-4 costs too much CPU
        // on the notebook in noisy dusk images (18 % of the frames were dropped). Opened on the recording thread:
        // starting ffmpeg or the video writer must not stall the capture thread.
        IVideoSink Open() => ffmpeg is not null && IsHardwareEncoder(options.Encoder)
            ? new FfmpegRecorder(ffmpeg, path, size, fps, options.Encoder, options.Quality, crop)
            : new VideoRecorder(path, size, fps, crop, motionJpeg: ffmpeg is not null);

        _logger.LogWarning(
            "RECORDING reference video {Path} until {End:HH:mm} (ADR-0005: contains personal data, private use only)", path, slot.End);
        (double, double)? band = crop is null ? null : (_options.BandTop!.Value, _options.BandBottom!.Value);
        return new Session(slot, path, Open, first.Timestamp, cameraSettings, band, _logger);
    }

    /// <param name="wait">How long to wait for the video to be completed (on shutdown); the pipeline never waits.</param>
    private void Stop(string reason, TimeSpan? wait = null)
    {
        if (_session is not { } session)
        {
            return;
        }

        _session = null;
        Task finish = Task.Run(() => Finish(session, reason));
        if (wait is TimeSpan timeout)
        {
            // On shutdown only the video itself must be complete; the conversion can be done later with "compress".
            finish.Wait(timeout);
        }
    }

    private void Finish(Session session, string reason)
    {
        try
        {
            session.Complete();
            if (session.Recorder is not { } recorder)
            {
                return; // could not be started, already logged
            }

            string path = session.Path;
            WriteMetadata(session, recorder);
            if (_siteConfigPath is not null && File.Exists(_siteConfigPath))
            {
                File.Copy(_siteConfigPath, path + ".site.json", overwrite: true);
            }

            _logger.LogWarning(
                "Recording {Path} finished ({Reason}): {Frames} frames, {Dropped} dropped by the recorder", path, reason, recorder.FramesWritten, session.Dropped);

            if (recorder is FfmpegRecorder direct)
            {
                long stored = VideoCompressor.CountFrames(path);
                _logger.LogInformation(
                    "Recording {Path}: written directly as H.264 ({Encoder}), {Stored} of {Written} frames in the file, {MB:N0} MB",
                    path, _options.Encoder, stored, direct.FramesWritten, new FileInfo(path).Length / 1024.0 / 1024.0);
            }
            else if (_ffmpeg is not null && reason == "service stopping")
            {
                // No conversion while the service shuts down: ffmpeg would outlive it and leave half a file.
                _logger.LogWarning("Recording {Path} stays in {Video}; convert it with CameraProbe compress", path, recorder.VideoPath);
            }
            else if (_ffmpeg is not null)
            {
                bool converted = VideoCompressor.Compress(
                    _ffmpeg, recorder.VideoPath, _options.Encoder, _options.Quality, keepOriginal: false,
                    message => _logger.LogInformation("{Message}", message.Trim()), lowPriority: true, output: path);
                if (converted)
                {
                    _logger.LogInformation("Recording {Path}: H.264 conversion done", path);
                }
                else
                {
                    _logger.LogWarning(
                        "Recording {Path}: H.264 conversion failed, the video stays in {Video}; convert it with CameraProbe compress",
                        path, recorder.VideoPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Finishing recording {Path} failed", session.Path);
        }
    }

    private static void WriteMetadata(Session session, IVideoSink recorder)
    {
        var metadata = new
        {
            Note = "Contains personal data – keep in the private repository only (ADR-0005).",
            Started = session.Started,
            Ended = session.LastFrame,
            Frames = recorder.FramesWritten,
            Dropped = session.Dropped,
            RecordedBy = "service",
            Tags = session.Slot.Tags ?? [],
            Camera = session.CameraSettings,
            // The video holds only these rows: the analysis must shift the geometry accordingly (SiteOptions.ForCrop).
            Band = session.Band is { } b ? new { b.Top, b.Bottom } : null,
        };
        File.WriteAllText(session.Path + ".json", JsonSerializer.Serialize(metadata, JsonOptions));
    }

    private static bool IsHardwareEncoder(string encoder) => encoder.EndsWith("_qsv", StringComparison.Ordinal) || encoder.EndsWith("_nvenc", StringComparison.Ordinal);

    private IReadOnlyList<RecordingSlot> ReadSchedule()
    {
        string path = Path.Combine(Directory, ScheduleFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<RecordingSlot>>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning("Recording schedule {Path} unreadable: {Message}", path, ex.Message);
            return _slots;
        }
    }

    /// <summary>One running recording: queue plus writer thread, which also opens the recorder.</summary>
    private sealed class Session
    {
        private readonly BlockingCollection<Frame> _queue = new(QueueCapacity);
        private readonly Thread _writer;
        private readonly Func<IVideoSink> _open;
        private readonly ILogger _logger;
        private long _dropped;
        private volatile bool _failed;

        public Session(
            RecordingSlot slot, string path, Func<IVideoSink> open, DateTimeOffset started, object? cameraSettings, (double Top, double Bottom)? band, ILogger logger)
        {
            Slot = slot;
            Path = path;
            _open = open;
            Started = started;
            LastFrame = started;
            CameraSettings = cameraSettings;
            Band = band;
            _logger = logger;
            _writer = new Thread(Write) { IsBackground = true, Name = "recording" };
            _writer.Start();
        }

        public RecordingSlot Slot { get; }

        public string Path { get; }

        /// <summary>The opened recorder; null until the writer thread opened it, or if that failed.</summary>
        public IVideoSink? Recorder { get; private set; }

        public DateTimeOffset Started { get; }

        public DateTimeOffset LastFrame { get; private set; }

        public object? CameraSettings { get; }

        /// <summary>Rows of the camera image kept in the video (fractions), or null for the whole image.</summary>
        public (double Top, double Bottom)? Band { get; }

        public long Dropped => Interlocked.Read(ref _dropped);

        public void Enqueue(Frame frame)
        {
            if (_failed)
            {
                return;
            }

            // The pipeline disposes its frame; the recorder gets its own copy.
            var copy = new Frame(frame.Image.Clone(), frame.Timestamp, frame.Sequence);
            if (!_queue.TryAdd(copy))
            {
                copy.Dispose();
                Interlocked.Increment(ref _dropped);
                return;
            }

            LastFrame = frame.Timestamp;
        }

        public void Complete()
        {
            _queue.CompleteAdding();
            _writer.Join();
            Recorder?.Dispose();
            _queue.Dispose();
        }

        private void Write()
        {
            IVideoSink recorder;
            try
            {
                recorder = _open();
                Recorder = recorder;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recording {Path} could not be started", Path);
                _failed = true;
                Drain();
                return;
            }

            try
            {
                foreach (Frame frame in _queue.GetConsumingEnumerable())
                {
                    using (frame)
                    {
                        recorder.Write(frame);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Writing the recording failed");
                Drain();
            }
        }

        private void Drain()
        {
            foreach (Frame frame in _queue.GetConsumingEnumerable())
            {
                frame.Dispose();
            }
        }
    }
}
