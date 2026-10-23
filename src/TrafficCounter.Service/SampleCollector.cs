using System.Text.Json;
using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;
using TrafficCounter.Core.Operations;
using TrafficCounter.Pipeline;
using TrafficCounter.Vision;

namespace TrafficCounter.Service;

/// <summary>Collection of training samples in operation (ADR-0026); off unless an end date is set.</summary>
public sealed class SampleCollectionOptions
{
    /// <summary>End of the collection, at most 30 days ahead; null or past: off.</summary>
    public DateTimeOffset? Until { get; set; }

    /// <summary>Target folder; default: %TRAFFICCOUNTER_PRIVATE%\datasets\inbox, else %ProgramData%\TrafficCounter\datasets\inbox.</summary>
    public string? Directory { get; set; }

    /// <summary>At most this many vehicles per day (three images each).</summary>
    public int DailyLimit { get; set; } = 200;

    /// <summary>
    /// Share of the daily limit available at any time of day; the rest becomes available evenly over the day, so a
    /// busy morning cannot use up the evening, dusk and night.
    /// </summary>
    public double BurstShare { get; set; } = 0.1;

    /// <summary>Random share of all passages, also a test set without selection bias.</summary>
    public double RandomShare { get; set; } = 0.02;

    /// <summary>Class votes less unanimous than this: the class wavers (vans between car and truck).</summary>
    public float ClassConfidenceBelow { get; set; } = 0.8f;

    /// <summary>Mean detector confidence of the track below this: unsure (night, rain, occlusion).</summary>
    public float DetectionConfidenceBelow { get; set; } = 0.5f;

    /// <summary>
    /// The same at night; null: unsure tracks are not collected at night. With the low night thresholds (ADR-0028) almost
    /// every vehicle is below 0.5 there, so a night value must be lower and is set from the measured distribution.
    /// </summary>
    public float? NightDetectionConfidenceBelow { get; set; }

    /// <summary>
    /// The entry and exit images are taken this share of the band width before and after the counting line, so slow
    /// vehicles and switched tracks do not show a different vehicle at the start or end of the track.
    /// </summary>
    public double ViewDistanceShare { get; set; } = 0.15;

    /// <summary>Track with more than this times the median number of frames: slow special vehicles.</summary>
    public double SlowTrackFactor { get; set; } = 2.0;

    /// <summary>
    /// Model that finds the people to pixelate, if the counting model has no person class (own model, ADR-0039),
    /// e.g. the COCO model yolo11s-640x192.onnx. It runs only on frames an image is taken from. Without it, and
    /// with a counting model that knows no people, no samples are collected.
    /// </summary>
    public string? PersonModelPath { get; set; }

    /// <summary>Track first seen closer to the line than this share of the image width: recognised late.</summary>
    public double LateStartShare { get; set; } = 0.12;

    /// <summary>Samples older than this many days are deleted (not yet labelled ones; labelled ones move elsewhere).</summary>
    public int RetentionDays { get; set; } = 90;

    public int JpegQuality { get; set; } = 90;
}

/// <summary>
/// Collects anonymised training samples for the own model (ADR-0026): for selected vehicles three still images of
/// the evaluation band (entry and exit at a fixed distance before and after the counting line, and at the line) plus the detector boxes as labelling
/// proposals. Images are anonymised (<see cref="Anonymizer"/>) and JPEG-encoded as soon as they are taken; only
/// those of selected vehicles are ever written. Writing runs in the background; counting never waits for it.
/// </summary>
internal sealed class SampleCollector
{
    private const int MaxDays = 30;
    private const float LineZoneShare = 0.04f;
    private static readonly TimeSpan LastImageInterval = TimeSpan.FromSeconds(0.7);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SampleCollectionOptions _options;
    private readonly ILogger _logger;
    private readonly Dictionary<int, TrackSamples> _tracks = [];
    private readonly Queue<int> _recentFrameCounts = new();
    private readonly Random _random = new();
    private DateOnly _day;
    private int _today;

    private SampleCollector(SampleCollectionOptions options, string directory, DateTimeOffset until, ILogger logger)
    {
        _options = options;
        Directory = directory;
        Until = until;
        _logger = logger;
    }

    public string Directory { get; }

    public DateTimeOffset Until { get; }

    /// <summary>Null unless the collection is switched on with an end date in the future.</summary>
    public static SampleCollector? Create(SampleCollectionOptions options, string defaultDirectory, DateTimeOffset now, ILogger logger)
    {
        if (options.Until is not DateTimeOffset until || until <= now)
        {
            return null;
        }

        if (until > now.AddDays(MaxDays))
        {
            logger.LogWarning("SampleCollection:Until {Until:yyyy-MM-dd} is more than {Days} days ahead; collecting until {Limit:yyyy-MM-dd} only", until, MaxDays, now.AddDays(MaxDays));
            until = now.AddDays(MaxDays);
        }

        string directory = Path.GetFullPath(options.Directory ?? defaultDirectory);
        RecordingPolicy.EnsureNotInPublicRepository(Path.Combine(directory, "x.jpg"));
        System.IO.Directory.CreateDirectory(directory);
        var collector = new SampleCollector(options, directory, until, logger);
        collector.DeleteExpired(now);
        return collector;
    }

    public bool IsActive(DateTimeOffset now) => now < Until;

    /// <summary>Called for every processed frame, after the pipeline.</summary>
    /// <param name="privateAreas">Pixelated completely (ignore zones: driveways with parked cars), frame coordinates.</param>
    public void Observe(Frame frame, FrameResult result, IReadOnlyList<BoundingBox> persons, IReadOnlyList<Rect> privateAreas, LightingCondition lighting) =>
        Observe(frame, result, () => persons, privateAreas, lighting);

    /// <summary>Called for every processed frame, after the pipeline.</summary>
    /// <param name="persons">
    /// Person boxes of this frame, in frame coordinates, for anonymising. Asked for only when an image is taken, so
    /// that a separate person model runs on a few frames a day instead of every frame.
    /// </param>
    /// <param name="privateAreas">Pixelated completely (ignore zones: driveways with parked cars), frame coordinates.</param>
    public void Observe(Frame frame, FrameResult result, Func<IReadOnlyList<BoundingBox>> persons, IReadOnlyList<Rect> privateAreas, LightingCondition lighting)
    {
        IReadOnlyList<BoundingBox>? personsOfFrame = null;
        IReadOnlyList<BoundingBox> Persons() => personsOfFrame ??= persons();

        DateTimeOffset now = frame.Timestamp;
        if (!IsActive(now))
        {
            foreach (TrackSamples samples in _tracks.Values)
            {
                samples.Dispose();
            }

            _tracks.Clear();
            return;
        }

        float lineX = (result.Line.Start.X + result.Line.End.X) / 2;
        foreach (TrackedObject obj in result.Tracking.Active)
        {
            if (!_tracks.TryGetValue(obj.TrackId, out TrackSamples? track))
            {
                track = new TrackSamples(Math.Abs(obj.Box.BottomCenter.X - lineX));
                _tracks[obj.TrackId] = track;
            }

            track.Frames++;
            track.ConfidenceSum += obj.Confidence;
            float distance = Math.Abs(obj.Box.BottomCenter.X - lineX);

            // Images only from frames the detector ran on, so that the person boxes for anonymising are current.
            if (!result.DetectorRan)
            {
                continue;
            }

            float viewDistance = (float)_options.ViewDistanceShare * result.Band.Width;
            if (track.First is null && distance <= viewDistance)
            {
                track.First = Take(frame, result, Persons(), privateAreas, now);
            }

            // At the line: only close to it, so that an approaching vehicle is not copied in every frame.
            if (distance < track.LineDistance && distance < LineZoneShare * result.Band.Width)
            {
                track.LineDistance = distance;
                track.Line?.Dispose();
                track.Line = Take(frame, result, Persons(), privateAreas, now);
            }
            else if (!track.ExitFixed && track.Line is not null && distance >= Math.Max(viewDistance, LineZoneShare * result.Band.Width))
            {
                // Past the line by the view distance: the exit image, kept from now on.
                track.Last?.Dispose();
                track.Last = Take(frame, result, Persons(), privateAreas, now);
                track.ExitFixed = true;
            }
            else if (!track.ExitFixed && distance >= LineZoneShare * result.Band.Width && now - track.LastTakenAt >= LastImageInterval)
            {
                // Until then the latest image away from the line, in case the track ends earlier.
                track.Last?.Dispose();
                track.Last = Take(frame, result, Persons(), privateAreas, now);
                track.LastTakenAt = now;
            }
        }

        foreach (VehiclePassageEvent passage in result.Passages)
        {
            if (_tracks.Remove(passage.TrackId, out TrackSamples? track))
            {
                List<string> reasons = Reasons(passage, track, result.Band.Width, lighting);
                Remember(passage.TrackLengthFrames);
                if (reasons.Count > 0)
                {
                    Store(track, reasons, passage, lighting, now);
                }
                else
                {
                    track.Dispose();
                }
            }
        }

        foreach (int removed in result.Tracking.RemovedTrackIds)
        {
            // Not counted: interesting only if it was recognised late (e.g. a tractor first seen past the line).
            if (_tracks.Remove(removed, out TrackSamples? track))
            {
                if (track.Frames >= 5 && track.FirstDistance < _options.LateStartShare * result.Band.Width)
                {
                    Store(track, ["not counted, recognised late"], null, lighting, now);
                }
                else
                {
                    track.Dispose();
                }
            }
        }
    }

    /// <summary>Why a counted vehicle is collected; empty: not collected.</summary>
    internal List<string> Reasons(VehiclePassageEvent passage, TrackSamples track, int imageWidth, LightingCondition lighting = LightingCondition.Day)
    {
        List<string> reasons = [];
        if (passage.ClassConfidence is float share && share < _options.ClassConfidenceBelow)
        {
            reasons.Add($"class wavers ({share:0.00})");
        }

        // At night only with its own, lower threshold: with the night thresholds (ADR-0028) almost every vehicle is
        // "unsure" by the day threshold, which would use up the daily limit before the morning.
        float? unsureBelow = lighting == LightingCondition.Night ? _options.NightDetectionConfidenceBelow : _options.DetectionConfidenceBelow;
        if (unsureBelow is float below && track.Frames > 0 && track.ConfidenceSum / track.Frames < below)
        {
            reasons.Add($"unsure detection ({track.ConfidenceSum / track.Frames:0.00})");
        }

        // Bicycles (including e-scooters) are frequent enough on the road, but few among the samples: the model misses
        // many of them, and own training data is meant to fix exactly that (roadmap 3c).
        if (passage.Class is VehicleClass.Truck or VehicleClass.Bus or VehicleClass.Motorcycle or VehicleClass.Agricultural or VehicleClass.Bicycle)
        {
            reasons.Add($"rare class {passage.Class}");
        }

        if (_recentFrameCounts.Count >= 20)
        {
            List<int> sorted = [.. _recentFrameCounts.Order()];
            if (passage.TrackLengthFrames > _options.SlowTrackFactor * sorted[sorted.Count / 2])
            {
                reasons.Add($"slow ({passage.TrackLengthFrames} frames, median {sorted[sorted.Count / 2]})");
            }
        }

        if (track.FirstDistance < _options.LateStartShare * imageWidth)
        {
            reasons.Add("recognised late");
        }

        if (passage.TrailerCount > 0)
        {
            reasons.Add($"{passage.TrailerCount} trailer(s)");
        }

        if (_random.NextDouble() < _options.RandomShare)
        {
            reasons.Add("random sample");
        }

        return reasons;
    }

    private void Remember(int frames)
    {
        _recentFrameCounts.Enqueue(frames);
        while (_recentFrameCounts.Count > 200)
        {
            _recentFrameCounts.Dequeue();
        }
    }

    /// <summary>
    /// Copy of the band with everything needed to anonymise it later, and all boxes of the frame (band coordinates)
    /// as labelling proposals. Only the copy is made on the pipeline thread; anonymising and JPEG encoding happen in
    /// the background and only for samples that are kept (ADR-0026: collecting must not slow down counting).
    /// </summary>
    private static Snapshot Take(Frame frame, FrameResult result, IReadOnlyList<BoundingBox> persons, IReadOnlyList<Rect> privateAreas, DateTimeOffset now)
    {
        Rect band = result.Band;
        using Mat crop = new(frame.Image, band);
        object[] boxes = [.. result.Tracking.Active.Select(a => (object)new
        {
            a.TrackId,
            Class = a.Class.ToString(),
            a.Confidence,
            X = a.Box.Left - band.X,
            Y = a.Box.Top - band.Y,
            a.Box.Width,
            a.Box.Height,
        })];
        return new Snapshot(
            crop.Clone(), band.Location, [.. result.Detections.Select(d => (d.Box, d.Class))], [.. persons], [.. privateAreas],
            now, band.Width, band.Height, boxes);
    }

    /// <summary>How many samples of the day may exist at this time of day: the burst share plus the elapsed part of the day.</summary>
    internal static int Allowance(int dailyLimit, double burstShare, TimeSpan timeOfDay)
    {
        double share = Math.Clamp(burstShare, 0, 1) + ((1 - Math.Clamp(burstShare, 0, 1)) * timeOfDay.TotalHours / 24);
        return Math.Min(dailyLimit, (int)Math.Ceiling((dailyLimit * share) - 1e-9));
    }

    private void Store(TrackSamples track, List<string> reasons, VehiclePassageEvent? passage, LightingCondition lighting, DateTimeOffset now)
    {
        var day = DateOnly.FromDateTime(now.LocalDateTime);
        if (day != _day)
        {
            _day = day;
            _today = CountExisting(day);
            DeleteExpired(now);
        }

        if (_today >= Allowance(_options.DailyLimit, _options.BurstShare, now.LocalDateTime.TimeOfDay) || track.First is null)
        {
            track.Dispose();
            return;
        }

        _today++;
        string folder = Path.Combine(Directory, $"{day:yyyy-MM-dd}", $"{now.LocalDateTime:HHmmss}-{passage?.TrackId ?? 0}");
        Snapshot?[] images = [track.First, track.Line, track.Last];
        string[] names = ["entry", "line", "exit"];
        var metadata = new
        {
            Note = "Anonymised training sample (ADR-0026) - keep in the private repository only.",
            Time = passage?.PassedAt ?? now,
            Direction = passage?.Direction.ToString(),
            Class = passage?.Class.ToString(),
            passage?.ClassConfidence,
            Trailers = passage?.TrailerCount,
            passage?.SpeedKmh,
            Lighting = lighting.ToString(),
            Reasons = reasons,
            Images = images.Select((s, i) => s is null ? null : new { File = names[i] + ".jpg", s.Time, s.Width, s.Height, Boxes = s.Boxes }).Where(i => i is not null),
        };

        // Anonymised, encoded and written in the background; a failure loses one sample, never counting.
        int quality = _options.JpegQuality;
        _ = Task.Run(() =>
        {
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                for (int i = 0; i < images.Length; i++)
                {
                    if (images[i] is { } image)
                    {
                        File.WriteAllBytes(Path.Combine(folder, names[i] + ".jpg"), image.Encode(quality));
                    }
                }

                File.WriteAllText(Path.Combine(folder, "sample.json"), JsonSerializer.Serialize(metadata, JsonOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OpenCVException)
            {
                _logger.LogWarning("Training sample {Folder} could not be written: {Message}", folder, ex.Message);
            }
            finally
            {
                track.Dispose();
            }
        });
        _logger.LogDebug("Training sample {Folder}: {Reasons}", folder, string.Join(", ", reasons));
    }

    private int CountExisting(DateOnly day)
    {
        string folder = Path.Combine(Directory, $"{day:yyyy-MM-dd}");
        return System.IO.Directory.Exists(folder) ? System.IO.Directory.GetDirectories(folder).Length : 0;
    }

    /// <summary>Deletes day folders older than the retention period (ADR-0026 limit 4).</summary>
    private void DeleteExpired(DateTimeOffset now)
    {
        DateOnly oldest = DateOnly.FromDateTime(now.LocalDateTime).AddDays(-_options.RetentionDays);
        foreach (string folder in System.IO.Directory.GetDirectories(Directory))
        {
            if (DateOnly.TryParseExact(Path.GetFileName(folder), "yyyy-MM-dd", out DateOnly day) && day < oldest)
            {
                try
                {
                    System.IO.Directory.Delete(folder, recursive: true);
                    _logger.LogInformation("Training samples of {Day:yyyy-MM-dd} deleted (older than {Days} days)", day, _options.RetentionDays);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning("Training samples {Folder} could not be deleted: {Message}", folder, ex.Message);
                }
            }
        }
    }

    /// <summary>Unanonymised band copy; <see cref="Encode"/> makes faces and plates unrecognisable before encoding.</summary>
    internal sealed record Snapshot(
        Mat Image, Point Origin, (BoundingBox Box, VehicleClass Class)[] Vehicles, BoundingBox[] Persons, Rect[] PrivateAreas,
        DateTimeOffset Time, int Width, int Height, object[] Boxes) : IDisposable
    {
        public byte[] Encode(int quality)
        {
            Anonymizer.Apply(Image, Vehicles, Persons, Origin, PrivateAreas);
            Cv2.ImEncode(".jpg", Image, out byte[] jpeg, new ImageEncodingParam(ImwriteFlags.JpegQuality, quality));
            return jpeg;
        }

        public void Dispose() => Image.Dispose();
    }

    internal sealed class TrackSamples(float firstDistance) : IDisposable
    {
        public float FirstDistance { get; } = firstDistance;

        public int Frames { get; set; }

        public float ConfidenceSum { get; set; }

        public float LineDistance { get; set; } = float.MaxValue;

        public DateTimeOffset LastTakenAt { get; set; }

        /// <summary>The exit image was taken at the view distance past the line and is kept.</summary>
        public bool ExitFixed { get; set; }

        public Snapshot? First { get; set; }

        public Snapshot? Line { get; set; }

        public Snapshot? Last { get; set; }

        public void Dispose()
        {
            First?.Dispose();
            Line?.Dispose();
            Last?.Dispose();
        }
    }
}
