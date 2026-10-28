using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Vision;

namespace TrafficCounter.Service;

/// <summary>Short videos of passages with a conspicuous speed, to check the measurement afterwards (ADR-0053).</summary>
public sealed class SpeedClipOptions
{
    /// <summary>
    /// End of the recording, at most 30 days ahead; null or past: off. The clips contain personal data like reference
    /// recordings, so the mode is enabled explicitly and for a limited time (ADR-0005 no. 3).
    /// </summary>
    public DateTimeOffset? Until { get; set; }

    public const int MaxDays = 30;

    /// <summary>True if <see cref="Until"/> lies after <paramref name="now"/> and at most <see cref="MaxDays"/> ahead.</summary>
    public bool IsActive(DateTimeOffset now) => Until is DateTimeOffset until && until > now && until <= now.AddDays(MaxDays) && Rules.Count > 0;

    /// <summary>Target folder (private). Default: the folder "speed" in the recording folder.</summary>
    public string? Directory { get; set; }

    /// <summary>A passage is recorded if any rule matches its class and speed.</summary>
    public List<SpeedClipRule> Rules { get; set; } = [];

    /// <summary>Video before the crossing, in seconds.</summary>
    public double SecondsBefore { get; set; } = 4;

    /// <summary>Video after the crossing, in seconds.</summary>
    public double SecondsAfter { get; set; } = 3;

    /// <summary>At most this many clips per day, so a miscalibrated scale cannot fill the disk.</summary>
    public int DailyLimit { get; set; } = 50;

    /// <summary>JPEG quality of the frames held in memory until a clip is written.</summary>
    public int JpegQuality { get; set; } = 85;
}

/// <summary>Speed range and classes of a <see cref="SpeedClipOptions"/> rule.</summary>
public sealed class SpeedClipRule
{
    /// <summary>Lowest speed, inclusive.</summary>
    public float MinKmh { get; set; }

    /// <summary>Upper end, exclusive; null: no upper end.</summary>
    public float? BelowKmh { get; set; }

    /// <summary>Classes the rule does not apply to, e.g. cars in a range only conspicuous for other classes.</summary>
    public List<VehicleClass> ExceptClasses { get; set; } = [];

    public bool Matches(VehicleClass vehicleClass, float kmh) =>
        kmh >= MinKmh && (BelowKmh is not float below || kmh < below) && !ExceptClasses.Contains(vehicleClass);
}

/// <summary>
/// Keeps the last seconds of the processed frames (the evaluation band, JPEG-compressed in memory) and writes a clip
/// around every passage that matches a rule: the frames from <see cref="SpeedClipOptions.SecondsBefore"/> before to
/// <see cref="SpeedClipOptions.SecondsAfter"/> after the crossing, with the timestamps, the passage and the site
/// geometry beside it, so the Annotator and the regression can open it like a reference recording. A passage is
/// emitted seconds after its crossing, so the frames before it are still in memory. Encoding and writing run on
/// their own threads; when they cannot keep up, frames are dropped from the clips, never from counting.
/// </summary>
internal sealed class SpeedClipRecorder : IDisposable
{
    private const int QueueCapacity = 30;

    // Passages are emitted up to a few seconds after the crossing (CountingSettings.EmitDelaySeconds) or later for a
    // vehicle waiting behind the line; older clip starts get what is left.
    private const double ExtraBufferSeconds = 10;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    private readonly SpeedClipOptions _options;
    private readonly (double Top, double Bottom)? _band;
    private readonly string? _siteConfigPath;
    private readonly string? _ffmpeg;
    private readonly string _encoder;
    private readonly int _quality;
    private readonly ILogger _logger;
    private readonly BlockingCollection<Frame> _queue = new(QueueCapacity);
    private readonly LinkedList<(DateTimeOffset At, byte[] Jpeg)> _buffer = [];
    private readonly List<Pending> _pending = [];
    private readonly List<Task> _writing = [];
    private readonly Lock _lock = new();
    private readonly Thread _encoderThread;
    private Size _bandSize;
    private DateOnly _day;
    private int _today;

    public SpeedClipRecorder(
        SpeedClipOptions options, RecordingOptions recording, string? siteConfigPath, string defaultRecordingDirectory, ILogger logger)
    {
        _options = options;
        _band = recording.BandTop is double top && recording.BandBottom is double bottom ? (top, bottom) : null;
        _siteConfigPath = siteConfigPath;
        _encoder = recording.Encoder;
        _quality = recording.Quality;
        _ffmpeg = recording.Encoder == "none" ? null : VideoCompressor.FindFfmpeg(string.IsNullOrEmpty(recording.FfmpegPath) ? null : recording.FfmpegPath);
        _logger = logger;
        Directory = Path.GetFullPath(options.Directory ?? Path.Combine(recording.Directory ?? defaultRecordingDirectory, "speed"));
        RecordingPolicy.EnsureNotInPublicRepository(Path.Combine(Directory, "x.mp4"));
        System.IO.Directory.CreateDirectory(Directory);
        _encoderThread = new Thread(Encode) { IsBackground = true, Name = "speed clips" };
        _encoderThread.Start();
    }

    public string Directory { get; }

    /// <summary>Clips written so far (complete with their metadata).</summary>
    public int Written { get; private set; }

    /// <summary>Called for every processed frame; the recorder keeps its own copy of the band.</summary>
    public void Offer(Frame frame)
    {
        Rect crop = _band is { } b ? RecordingPolicy.BandRect(frame.Image.Size(), b.Top, b.Bottom) : new Rect(0, 0, frame.Image.Width, frame.Image.Height);
        Mat copy;
        using (var region = new Mat(frame.Image, crop))
        {
            copy = region.Clone();
        }

        var item = new Frame(copy, frame.Timestamp, frame.Sequence);
        if (_queue.IsAddingCompleted || !_queue.TryAdd(item))
        {
            item.Dispose();
        }
    }

    /// <summary>Schedules a clip if the passage matches a rule and today's limit is not reached.</summary>
    public void OnPassage(VehiclePassageEvent passage)
    {
        if (passage.SpeedKmh is not float kmh || passage.PassedAt >= _options.Until || !_options.Rules.Any(r => r.Matches(passage.Class, kmh)))
        {
            return;
        }

        lock (_lock)
        {
            var day = DateOnly.FromDateTime(passage.PassedAt.LocalDateTime);
            if (day != _day)
            {
                (_day, _today) = (day, 0);
            }

            if (_today >= _options.DailyLimit)
            {
                return;
            }

            _today++;
            _pending.Add(new Pending(passage, passage.PassedAt.AddSeconds(-_options.SecondsBefore), passage.PassedAt.AddSeconds(_options.SecondsAfter)));
        }
    }

    /// <summary>Writes the pending clips with the frames there are and waits for all clips (up to 30 s).</summary>
    public void Dispose()
    {
        _queue.CompleteAdding();
        _encoderThread.Join();
        lock (_lock)
        {
            foreach (Pending pending in _pending)
            {
                StartWriting(pending);
            }

            _pending.Clear();
        }

        Task.WaitAll([.. _writing], TimeSpan.FromSeconds(30));
        _queue.Dispose();
    }

    private void Encode()
    {
        int[] parameters = [(int)ImwriteFlags.JpegQuality, _options.JpegQuality];
        foreach (Frame frame in _queue.GetConsumingEnumerable())
        {
            using (frame)
            {
                try
                {
                    Cv2.ImEncode(".jpg", frame.Image, out byte[] jpeg, parameters);
                    lock (_lock)
                    {
                        _bandSize = frame.Image.Size();
                        _buffer.AddLast((frame.Timestamp, jpeg));
                        DateTimeOffset keepFrom = frame.Timestamp.AddSeconds(-(_options.SecondsBefore + _options.SecondsAfter + ExtraBufferSeconds));
                        while (_buffer.First is { } first && first.Value.At < keepFrom)
                        {
                            _buffer.RemoveFirst();
                        }

                        foreach (Pending ready in _pending.Where(p => frame.Timestamp >= p.To).ToList())
                        {
                            _pending.Remove(ready);
                            StartWriting(ready);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Buffering a frame for speed clips failed");
                }
            }
        }
    }

    /// <summary>Takes the frames of the clip from the buffer (under the lock) and writes them on another thread.</summary>
    private void StartWriting(Pending pending)
    {
        List<(DateTimeOffset At, byte[] Jpeg)> frames = [.. _buffer.Where(f => f.At >= pending.From && f.At <= pending.To)];
        if (frames.Count < 2)
        {
            _logger.LogWarning("Speed clip for the passage at {PassedAt:HH:mm:ss} skipped: no frames buffered", pending.Passage.PassedAt);
            return;
        }

        Size size = _bandSize;
        _writing.RemoveAll(t => t.IsCompleted);
        _writing.Add(Task.Run(() => Write(pending.Passage, frames, size)));
    }

    private void Write(VehiclePassageEvent passage, List<(DateTimeOffset At, byte[] Jpeg)> frames, Size size)
    {
        string name = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{passage.PassedAt.LocalDateTime:yyyyMMdd-HHmmss}-{passage.Class}-{passage.SpeedKmh:0}kmh.mp4");
        string path = Path.Combine(Directory, name);
        try
        {
            double seconds = (frames[^1].At - frames[0].At).TotalSeconds;
            double fps = seconds > 0 ? (frames.Count - 1) / seconds : 15;
            string video;
            using (var recorder = new VideoRecorder(path, size, fps, motionJpeg: _ffmpeg is not null))
            {
                video = recorder.VideoPath;
                long sequence = 0;
                foreach ((DateTimeOffset at, byte[] jpeg) in frames)
                {
                    using var image = Cv2.ImDecode(jpeg, ImreadModes.Color);
                    using var frame = new Frame(image.Clone(), at, sequence++);
                    recorder.Write(frame);
                }
            }

            WriteMetadata(path, passage, frames);
            if (_siteConfigPath is not null && File.Exists(_siteConfigPath))
            {
                File.Copy(_siteConfigPath, path + ".site.json", overwrite: true);
            }

            if (_ffmpeg is not null && !VideoCompressor.Compress(_ffmpeg, video, _encoder, _quality, keepOriginal: false, lowPriority: true, output: path))
            {
                _logger.LogWarning("Speed clip {Path}: H.264 conversion failed, the video stays in {Video}", path, video);
            }

            lock (_lock)
            {
                Written++;
            }

            _logger.LogWarning(
                "Speed clip {Path}: {Class} at {Speed:0.0} km/h, {Frames} frames (ADR-0005: contains personal data, private use only)",
                path, passage.Class, passage.SpeedKmh, frames.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Writing speed clip {Path} failed", path);
        }
    }

    private void WriteMetadata(string path, VehiclePassageEvent passage, List<(DateTimeOffset At, byte[] Jpeg)> frames)
    {
        var metadata = new
        {
            Note = "Contains personal data – keep in the private repository only (ADR-0005).",
            Started = frames[0].At,
            Ended = frames[^1].At,
            Frames = frames.Count,
            Dropped = 0,
            RecordedBy = "speed clip",
            Tags = new[] { "speed-clip" },
            Camera = (object?)null,
            // The video holds only these rows: the analysis must shift the geometry accordingly (SiteOptions.ForCrop).
            Band = _band is { } b ? new { b.Top, b.Bottom } : null,
            Passage = new
            {
                passage.PassedAt,
                OffsetSeconds = (passage.PassedAt - frames[0].At).TotalSeconds,
                passage.Direction,
                passage.Class,
                passage.SpeedKmh,
                passage.LinePosition,
                passage.TrackId,
            },
        };
        File.WriteAllText(path + ".json", JsonSerializer.Serialize(metadata, JsonOptions));
    }

    private sealed record Pending(VehiclePassageEvent Passage, DateTimeOffset From, DateTimeOffset To);
}
