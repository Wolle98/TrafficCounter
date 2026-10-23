using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Detection.Onnx;
using TrafficCounter.Evaluation;
using TrafficCounter.Pipeline;
using TrafficCounter.Tracking;
using TrafficCounter.Vision;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

Environment.SetEnvironmentVariable("OPENCV_LOG_LEVEL", "ERROR");

Dictionary<string, string> options = [];
for (int i = 0; i + 1 < args.Length; i += 2)
{
    options[args[i].TrimStart('-')] = args[i + 1];
}

if (args is ["-h" or "--help", ..])
{
    Console.WriteLine(
        """
        TrafficCounter regression – counts all reference videos that have ground truth and compares.

        Usage:
          TrafficCounter.Regression [--recordings <dir>] [--config <site.json>] [--model <model.onnx>]
                                    [--provider Auto|DirectML|Cpu|OpenVino] [--tolerance 1.5]
                                    [--video <video.mp4>] [--new-track-confidence 0.6] [--high-confidence 0.5]
                                    [--max-fps 0] [--motion-gate on|off] [--threads 0] [--openvino-device GPU|CPU|NPU|AUTO]
                                    [--night-confidence 0.2/0.25] [--enhance none|clahe[:clip]|gamma[:gamma]|grayscale] [--enhance-from night|dusk|day]
                                    [--resize linear|area] [--provider TensorRT|Cuda --nvidia-libs <dir;dir>]
          (load settings as in the service, ADR-0029; the run also reports the computing load)
          TrafficCounter.Regression --video <video.mp4> --trace <mm:ss-mm:ss> [--config ...] [--model ...]
          TrafficCounter.Regression --live true [--database SqlServer|PostgreSql] [--video ...] [--recordings ...]
                                    (compares the hand count with what the running service stored, ADR-0032)
          TrafficCounter.Regression --speed true [--live true [--database ...]] [--video ...] [--recordings ...]
                                    (test drives with known speed against the measured speed, ADR-0035)
          TrafficCounter.Regression --calibrate-speed true [--car-length 4.5] [--video ...] [--recordings ...]
                                    (speed scale from the box widths of cars at the counting line, ADR-0015)
          TrafficCounter.Regression --count true --video <video.mp4> [--interval 10]
                                    (only counts, per interval and direction, e.g. to compare two cameras)
          TrafficCounter.Regression --extract-samples Bicycle [--video ...] [--recordings ...] [--out <inbox>] [--recut true]
                                    (training samples of one class from the hand counts: three anonymised images
                                    per passage with the detector's boxes as proposals, into datasets/inbox; ADR-0026;
                                    --recut cuts the existing samples again in place, e.g. after a geometry fix)
          TrafficCounter.Regression --prelabel <inbox> --model <teacher.onnx>
                                    (stores a stronger model's boxes in each training sample as TeacherBoxes,
                                    proposed by the labelling tasks instead of the counting model's boxes)
          TrafficCounter.Regression --mine-with <teacher.onnx> [--video ...] [--recordings ...] [--out <inbox>]
                                    (counts each recording with the production model and a stronger teacher model,
                                    e.g. yolo11x; passages only the teacher counted become training samples)
                                    (prints detections and tracks frame by frame for one section)

        Defaults: recordings = %TRAFFICCOUNTER_PRIVATE%\recordings, config = %TRAFFICCOUNTER_CONFIG%,
        model = Detector:ModelPath from the config. A <video>.site.json next to a video (written by record)
        replaces the config's geometry for that video. Exit code 1 if an acceptance target is missed.
        """);
    return 0;
}

string recordings = options.GetValueOrDefault("recordings")
    ?? Path.Combine(PrivateLocations.PrivateRoot ?? ".", "recordings");
string configPath = options.GetValueOrDefault("config") ?? PrivateLocations.ConfigPath
    ?? throw new ArgumentException("Pass --config or set TRAFFICCOUNTER_CONFIG.");
var processing = new ProcessingOptions();
if (options.TryGetValue("max-fps", out string? maxFps))
{
    processing.MaxFps = double.Parse(maxFps, CultureInfo.InvariantCulture);
}

processing.MotionGate.Enabled = options.GetValueOrDefault("motion-gate") is "on" or "true";

TrackerSettings trackerSettings = new();
if (options.TryGetValue("new-track-confidence", out string? newTrack))
{
    trackerSettings = trackerSettings with { NewTrackConfidence = float.Parse(newTrack, CultureInfo.InvariantCulture) };
}

if (options.TryGetValue("two-wheeler-confidence", out string? twoWheeler))
{
    // "high/new", e.g. 0.3/0.35: thresholds for bicycles and motorcycles.
    string[] parts = twoWheeler.Split('/');
    trackerSettings = trackerSettings with
    {
        TwoWheelerHighConfidence = float.Parse(parts[0], CultureInfo.InvariantCulture),
        TwoWheelerNewTrackConfidence = float.Parse(parts[^1], CultureInfo.InvariantCulture),
    };
}

if (options.TryGetValue("night-confidence", out string? night))
{
    // "high/new", e.g. 0.15/0.2: tracker thresholds at night (ADR-0028).
    string[] parts = night.Split('/');
    trackerSettings = trackerSettings with
    {
        NightHighConfidence = float.Parse(parts[0], CultureInfo.InvariantCulture),
        NightNewTrackConfidence = float.Parse(parts[^1], CultureInfo.InvariantCulture),
    };
}

if (options.TryGetValue("enhance", out string? enhance))
{
    // "clahe", "clahe:4" (clip limit), "gamma" or "gamma:0.6"; --enhance-from night|dusk|day.
    string[] parts = enhance.Split(':');
    processing.Enhancement.Mode = Enum.Parse<EnhancementMode>(parts[0], ignoreCase: true);
    if (parts.Length > 1)
    {
        double value = double.Parse(parts[1], CultureInfo.InvariantCulture);
        if (processing.Enhancement.Mode == EnhancementMode.Clahe)
        {
            processing.Enhancement.ClipLimit = value;
        }
        else
        {
            processing.Enhancement.Gamma = value;
        }
    }

    if (options.TryGetValue("enhance-from", out string? from))
    {
        processing.Enhancement.From = Enum.Parse<TrafficCounter.Core.Operations.LightingCondition>(from, ignoreCase: true);
    }
}

if (options.TryGetValue("high-confidence", out string? high))
{
    trackerSettings = trackerSettings with { HighConfidence = float.Parse(high, CultureInfo.InvariantCulture) };
}
TimeSpan tolerance = TimeSpan.FromSeconds(double.Parse(options.GetValueOrDefault("tolerance", "1.5"), CultureInfo.InvariantCulture));

SiteOptions site = SiteConfigurationFile.Read(configPath);
YoloDetectorOptions detectorOptions = ReadDetectorOptions(configPath);
SpeedometerCalibration speedometer = ReadSpeedometer(configPath);
if (options.TryGetValue("model", out string? model))
{
    detectorOptions.ModelPath = model;
}

if (options.TryGetValue("openvino-device", out string? openVinoDevice))
{
    detectorOptions.OpenVinoDevice = openVinoDevice;
}

if (options.TryGetValue("threads", out string? threads))
{
    detectorOptions.IntraOpThreads = int.Parse(threads, CultureInfo.InvariantCulture);
}

if (options.TryGetValue("resize", out string? resize))
{
    detectorOptions.Resize = Enum.Parse<ResizeMethod>(resize, ignoreCase: true);
}

if (options.TryGetValue("provider", out string? provider))
{
    detectorOptions.ExecutionProvider = Enum.Parse<ExecutionProviderPreference>(provider, ignoreCase: true);
}

if (options.TryGetValue("nvidia-libs", out string? nvidiaLibs))
{
    // NVIDIA build (ADR-0041): folders with the CUDA, cuDNN and TensorRT libraries, separated by ';'.
    detectorOptions.NvidiaLibraryPaths = [.. nvidiaLibs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}

bool speedMode = options.GetValueOrDefault("speed") is "true" or "on";
bool liveMode = options.GetValueOrDefault("live") is "true" or "on";
List<string> videos = options.TryGetValue("video", out string? single)
    ? [single]
    : Directory.Exists(recordings)
        ? [.. Directory.EnumerateFiles(recordings, "*.mp4").Where(v => speedMode ? SpeedTruth(v, speedometer).Count > 0 : File.Exists(GroundTruthFile.PathFor(v))).Order()]
        : [];
if (videos.Count == 0)
{
    Console.WriteLine(speedMode
        ? $"No videos with test drives (*.speed.csv or speeds in *.gt.csv) in {recordings}."
        : $"No videos with ground truth (*.gt.csv) in {recordings}.");
    return 0;
}

// Test drives with known speed against the measured speed (ADR-0035), from the service's database or a new run.
if (speedMode && liveMode)
{
    var database = Enum.Parse<TrafficCounter.Data.DatabaseProvider>(options.GetValueOrDefault("database", "SqlServer"), ignoreCase: true);
    List<SpeedCheck> all = [];
    PrintSpeedometer(speedometer);
    foreach (string video in videos)
    {
        if (await TrafficCounter.Regression.LiveComparison.LoadAsync(video, SiteFor(video), database) is var (detected, toDirection))
        {
            all.AddRange(ReportSpeeds(video, SpeedEvaluator.Evaluate(SpeedTruth(video, speedometer), toDirection, detected, tolerance), speedometer));
        }
    }

    PrintSpeedSummary(all);
    return 0;
}

// Hand count against what the running service stored for the same time (ADR-0032); no detection run.
if (liveMode)
{
    var database = Enum.Parse<TrafficCounter.Data.DatabaseProvider>(options.GetValueOrDefault("database", "SqlServer"), ignoreCase: true);
    List<(string Video, string[] Tags, EvaluationResult Result)> live = [];
    foreach (string video in videos)
    {
        (string[] tags, _) = ReadMetadata(video);
        if (await TrafficCounter.Regression.LiveComparison.EvaluateAsync(
                video, SiteFor(video), GroundTruthFile.Read(GroundTruthFile.PathFor(video)), tolerance, database) is { } result)
        {
            live.Add((Path.GetFileName(video), tags, result));
            Console.WriteLine($"{Path.GetFileName(video),-28} {string.Join(',', tags),-24} {Format(result)}");
            PrintDetails(result);
        }
    }

    if (live.Count > 1)
    {
        Console.WriteLine($"  {"all",-20} {Format(EvaluationResult.Combine(live.Select(r => r.Result)))}");
    }

    return 0;
}

// People are only needed to anonymise extracted training samples (ADR-0026).
detectorOptions.ReportPersons = options.ContainsKey("extract-samples");
using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning));
YoloOnnxDetector detector;
try
{
    detector = new YoloOnnxDetector(detectorOptions, loggerFactory.CreateLogger<YoloOnnxDetector>());
}
catch (FileNotFoundException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

if (options.TryGetValue("trace", out string? traceRange))
{
    string video = options.GetValueOrDefault("video") ?? throw new ArgumentException("--trace needs --video <video.mp4>.");
    using (detector)
    {
        string snapshot = video + ".site.json";
        SiteOptions traceSite = File.Exists(snapshot) ? SiteConfigurationFile.Read(snapshot) : site;
        (_, (float Top, float Bottom)? traceBand) = ReadMetadata(video);
        Trace(video, traceBand is { } tb ? traceSite.ForCrop(tb.Top, tb.Bottom) : traceSite, detector, traceRange, trackerSettings);
    }

    return 0;
}

// Counts only, without a hand count: e.g. to compare a new camera with what another one counted at the same time.
if (options.GetValueOrDefault("count") is "true" or "on")
{
    int interval = int.Parse(options.GetValueOrDefault("interval", "10"), CultureInfo.InvariantCulture);
    using (detector)
    {
        foreach (string video in videos)
        {
            (_, string load, List<DetectedPassage> detected, _) = await EvaluateAsync(video, SiteFor(video), detector, [], tolerance, trackerSettings, processing);
            DateTimeOffset start = FrameTimestampsFile.TryRead(video) is { Count: > 0 } stamps ? stamps[0] : DateTimeOffset.MinValue;
            Console.WriteLine($"{Path.GetFileName(video)}: {detected.Count} passages ({load})");
            Console.WriteLine($"  {"from",-6} {"all",5} {"A",5} {"B",5}  by class");
            foreach (var bin in detected.GroupBy(d => (int)(d.Offset.TotalMinutes / interval)).OrderBy(g => g.Key))
            {
                DateTimeOffset from = start + TimeSpan.FromMinutes(bin.Key * interval);
                string classes = string.Join(", ", bin.GroupBy(d => d.Class).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"));
                Console.WriteLine($"  {from.LocalDateTime:HH:mm} {bin.Count(),5} {bin.Count(d => d.Direction == TravelDirection.A),5} {bin.Count(d => d.Direction == TravelDirection.B),5}  {classes}");
            }
        }
    }

    return 0;
}

if (options.TryGetValue("prelabel", out string? prelabelInbox))
{
    // Teacher boxes for the labelling tasks: --prelabel <inbox> --model <teacher.onnx>.
    using (detector)
    {
        RecordingPolicy.EnsureNotInPublicRepository(Path.Combine(prelabelInbox, "x.jpg"));
        return TrafficCounter.Regression.Prelabel.Run(prelabelInbox, detector, Path.GetFileNameWithoutExtension(detectorOptions.ModelPath));
    }
}

if (options.TryGetValue("mine-with", out string? teacherModel))
{
    // Teacher mining: count each recording with the production model and with a stronger "teacher" model; passages
    // only the teacher counted are the vehicles the production model misses, and become training samples.
    using (detector)
    {
        YoloDetectorOptions teacherOptions = ReadDetectorOptions(configPath);
        teacherOptions.ModelPath = teacherModel;
        teacherOptions.ExecutionProvider = detectorOptions.ExecutionProvider;
        teacherOptions.Resize = detectorOptions.Resize;
        teacherOptions.ReportPersons = true;
        using var teacher = new YoloOnnxDetector(teacherOptions, loggerFactory.CreateLogger<YoloOnnxDetector>());
        string inbox = options.GetValueOrDefault("out")
            ?? Path.Combine(PrivateLocations.PrivateRoot ?? throw new ArgumentException("Pass --out or set TRAFFICCOUNTER_PRIVATE."), "datasets", "inbox");
        RecordingPolicy.EnsureNotInPublicRepository(inbox);
        Dictionary<string, List<GroundTruthEntry>> mined = [];
        foreach (string video in videos)
        {
            (_, _, List<DetectedPassage> student, Func<ImageMotion, TravelDirection> toDirection) =
                await EvaluateAsync(video, SiteFor(video), detector, [], tolerance, trackerSettings, processing);
            (_, _, List<DetectedPassage> taught, _) = await EvaluateAsync(video, SiteFor(video), teacher, [], tolerance, trackerSettings, processing);
            List<GroundTruthEntry> only = [.. taught
                .Where(t => !student.Any(s => s.Direction == t.Direction && (s.Offset - t.Offset).Duration() <= tolerance))
                .Select(t => new GroundTruthEntry(t.Offset, toDirection(ImageMotion.LeftToRight) == t.Direction ? ImageMotion.LeftToRight : ImageMotion.RightToLeft, t.Class))];
            Console.WriteLine($"{Path.GetFileName(video)}: production {student.Count}, teacher {taught.Count}, only the teacher {only.Count}"
                + (only.Count > 0 ? $" ({string.Join(", ", only.GroupBy(o => o.Class).Select(g => $"{g.Key} {g.Count()}"))})" : ""));
            mined[video] = only;
        }

        return TrafficCounter.Regression.SampleExtraction.Run([.. mined.Keys], SiteFor, detector, trackerSettings, VehicleClass.Unknown, inbox,
            passagesFor: video => mined[video], reason: $"only the teacher model counted it ({Path.GetFileNameWithoutExtension(teacherModel)})");
    }
}

if (options.TryGetValue("extract-samples", out string? extractClass))
{
    using (detector)
    {
        string inbox = options.GetValueOrDefault("out")
            ?? Path.Combine(PrivateLocations.PrivateRoot ?? throw new ArgumentException("Pass --out or set TRAFFICCOUNTER_PRIVATE."), "datasets", "inbox");
        RecordingPolicy.EnsureNotInPublicRepository(inbox); // the images show the site
        return TrafficCounter.Regression.SampleExtraction.Run(videos, SiteFor, detector, trackerSettings, Enum.Parse<VehicleClass>(extractClass, ignoreCase: true), inbox,
            recut: options.GetValueOrDefault("recut") is "true");
    }
}

if (options.GetValueOrDefault("calibrate-speed") is "true" or "on")
{
    using (detector)
    {
        float carLength = float.Parse(options.GetValueOrDefault("car-length", "4.5"), CultureInfo.InvariantCulture);
        return await TrafficCounter.Regression.SpeedCalibration.RunAsync(videos, SiteFor, detector, trackerSettings, processing, carLength);
    }
}

if (speedMode)
{
    List<SpeedCheck> all = [];
    PrintSpeedometer(speedometer);
    using (detector)
    {
        foreach (string video in videos)
        {
            (_, _, List<DetectedPassage> detected, Func<ImageMotion, TravelDirection> toDirection) = await EvaluateAsync(
                video, SiteFor(video), detector, [], tolerance, trackerSettings, processing);
            all.AddRange(ReportSpeeds(video, SpeedEvaluator.Evaluate(SpeedTruth(video, speedometer), toDirection, detected, tolerance), speedometer));
        }
    }

    PrintSpeedSummary(all);
    return 0;
}

List<(string Video, string[] Tags, EvaluationResult Result)> results = [];
using (detector)
{
    foreach (string video in videos)
    {
        (string[] tags, _) = ReadMetadata(video);
        SiteOptions videoSite = SiteFor(video);
        (EvaluationResult result, string load, _, _) = await EvaluateAsync(
            video, videoSite, detector, GroundTruthFile.Read(GroundTruthFile.PathFor(video)), tolerance, trackerSettings, processing);
        results.Add((Path.GetFileName(video), tags, result));
        Console.WriteLine($"{Path.GetFileName(video),-28} {string.Join(',', tags),-24} {Format(result)}");
        PrintDetails(result);
        Console.WriteLine($"  load: {load}");
    }
}

Console.WriteLine();
Console.WriteLine("By category:");
foreach (string tag in results.SelectMany(r => r.Tags).Distinct().Order())
{
    Console.WriteLine($"  {tag,-20} {Format(EvaluationResult.Combine(results.Where(r => r.Tags.Contains(tag)).Select(r => r.Result)))}");
}

EvaluationResult total = EvaluationResult.Combine(results.Select(r => r.Result));
Console.WriteLine($"  {"all",-20} {Format(total)}");

// Acceptance targets from the roadmap (stage 1, ADR-0023).
(string Tags, double Target)[] targets = [("day,dry", 0.95), ("night,dry", 0.85), ("day,rain", 0.90), ("night,rain", 0.80)];
bool passed = true;
Console.WriteLine();
Console.WriteLine("Acceptance targets:");
foreach ((string tagList, double target) in targets)
{
    string[] required = tagList.Split(',');
    List<EvaluationResult> matching = [.. results.Where(r => required.All(r.Tags.Contains)).Select(r => r.Result)];
    if (matching.Count == 0)
    {
        Console.WriteLine($"  {tagList,-12} target {target:P0}: no videos yet");
        continue;
    }

    double accuracy = EvaluationResult.Combine(matching).Accuracy;
    passed &= accuracy >= target;
    Console.WriteLine($"  {tagList,-12} target {target:P0}: {accuracy:P1} {(accuracy >= target ? "OK" : "MISSED")}");
}

return passed ? 0 : 1;

// The calibration at recording time (written by record) wins over the current one, which may differ after the
// camera was realigned. The speed scale is taken from the current configuration if the snapshot predates it.
SiteOptions SiteFor(string video)
{
    (_, (float Top, float Bottom)? band) = ReadMetadata(video);
    string snapshot = video + ".site.json";
    SiteOptions recordedSite = File.Exists(snapshot) ? SiteConfigurationFile.Read(snapshot) : site;
    recordedSite.FarKerbMetersPerImageWidth ??= site.FarKerbMetersPerImageWidth;
    recordedSite.NearKerbMetersPerImageWidth ??= site.NearKerbMetersPerImageWidth;
    return band is { } b ? recordedSite.ForCrop(b.Top, b.Bottom) : recordedSite;
}

// Test drives of a video: the separate speed file plus annotated passages with a speed in the ground truth. The files
// hold the speedometer reading at the line; the true speed comes from the speedometer calibration.
static List<GroundTruthEntry> SpeedTruth(string video, SpeedometerCalibration speedometer)
{
    List<GroundTruthEntry> drives = [];
    foreach (string path in new[] { GroundTruthFile.SpeedPathFor(video), GroundTruthFile.PathFor(video) })
    {
        if (File.Exists(path))
        {
            drives.AddRange(GroundTruthFile.Read(path)
                .Where(e => e.SpeedKmh is not null)
                .Select(e => e with { SpeedKmh = speedometer.ToTrue(e.SpeedKmh!.Value) }));
        }
    }

    return drives;
}

static void PrintSpeedometer(SpeedometerCalibration speedometer) => Console.WriteLine(speedometer.IsIdentity
    ? "No speedometer calibration (TrafficCounter:SpeedTest in the config): the entered speeds are taken as true speeds."
    : string.Create(CultureInfo.InvariantCulture, $"Speedometer calibration: true = {speedometer.Factor:0.000} x reading {speedometer.OffsetKmh:+0.0;-0.0} km/h"));

static List<SpeedCheck> ReportSpeeds(string video, List<SpeedCheck> checks, SpeedometerCalibration speedometer)
{
    Console.WriteLine($"{Path.GetFileName(video)}: {checks.Count} test drives");
    foreach (SpeedCheck c in checks)
    {
        TimeSpan t = c.Drive.Offset;
        string measured = c.Passage is null
            ? "not counted"
            : c.Passage.SpeedKmh is float kmh
                ? string.Create(CultureInfo.InvariantCulture, $"measured {kmh,5:0.0} km/h  error {c.Error,5:+0.0;-0.0} km/h ({c.RelativeError,6:+0.0%;-0.0%})")
                : "counted without speed";
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds / 100} {c.Drive.Motion,-11} speedometer {speedometer.ToReading(c.Drive.SpeedKmh!.Value),3:0} -> true {c.Drive.SpeedKmh,5:0.0} km/h  {measured}  {c.Drive.Note}"));
    }

    return checks;
}

// Drives with a note were disturbed (braked, overtaken, ...) and are summarised separately, so they do not distort
// the scale check.
static void PrintSpeedSummary(List<SpeedCheck> checks)
{
    Console.WriteLine();
    PrintSpeedGroup("Speed", [.. checks.Where(c => string.IsNullOrWhiteSpace(c.Drive.Note))]);
    List<SpeedCheck> noted = [.. checks.Where(c => !string.IsNullOrWhiteSpace(c.Drive.Note))];
    if (noted.Count > 0)
    {
        PrintSpeedGroup("Drives with a note, left out above", noted);
    }
}

static void PrintSpeedGroup(string title, List<SpeedCheck> checks)
{
    SpeedSummary s = SpeedSummary.Of(checks);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{title}: {s.Measured}/{s.Drives} drives measured, mean error {s.MeanRelativeError:+0.0%;-0.0%}, mean absolute {s.MeanAbsoluteRelativeError:0.0%}, max {s.MaxAbsoluteRelativeError:0.0%}"));
    foreach (var group in checks.Where(c => c.RelativeError is not null).GroupBy(c => c.Drive.Motion))
    {
        SpeedSummary g = SpeedSummary.Of([.. group]);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {group.Key,-11} n={g.Measured}  mean error {g.MeanRelativeError:+0.0%;-0.0%}  mean absolute {g.MeanAbsoluteRelativeError:0.0%}"));
    }
}

static async Task<(EvaluationResult Result, string Load, List<DetectedPassage> Detected, Func<ImageMotion, TravelDirection> ToDirection)> EvaluateAsync(
    string video, SiteOptions site, IObjectDetector detector, List<GroundTruthEntry> truth, TimeSpan tolerance,
    TrackerSettings trackerSettings, ProcessingOptions processing)
{
    using var pipeline = new CountingPipeline(detector, new ByteTracker(trackerSettings), site, processing);
    List<DetectedPassage> detected = [];
    List<float> speeds = [];
    // Mean detector confidence per track, as the sample collection judges "unsure" tracks (ADR-0026).
    Dictionary<int, (double Sum, int Frames)> trackConfidence = [];
    List<float> countedConfidence = [];
    DateTimeOffset? first = null;
    DateTimeOffset last = default;
    Size size = default;
    long frames = 0;
    long processed = 0;
    double detectMilliseconds = 0;
    var wall = System.Diagnostics.Stopwatch.StartNew();
    await using (var source = new FileFrameSource(video, FilePlayback.Sequential))
    {
        while (source.TryRead(TimeSpan.Zero, out Frame? frame))
        {
            using (frame)
            {
                frames++;
                first ??= frame!.Timestamp;
                last = frame!.Timestamp;
                size = frame.Image.Size();
                if (!pipeline.ShouldProcess(frame.Timestamp))
                {
                    continue;
                }

                processed++;
                FrameResult result = pipeline.Process(frame);
                detectMilliseconds += result.DetectionTime.TotalMilliseconds;
                foreach (TrackedObject obj in result.Tracking.Active)
                {
                    (double sum, int n) = trackConfidence.GetValueOrDefault(obj.TrackId);
                    trackConfidence[obj.TrackId] = (sum + obj.Confidence, n + 1);
                }

                countedConfidence.AddRange(result.Passages
                    .Where(p => trackConfidence.ContainsKey(p.TrackId))
                    .Select(p => (float)(trackConfidence[p.TrackId].Sum / trackConfidence[p.TrackId].Frames)));
                detected.AddRange(result.Passages.Select(p => new DetectedPassage(p.PassedAt - first.Value, p.Direction, p.Class, p.SpeedKmh)));
                speeds.AddRange(result.Passages.Where(p => p.Class == VehicleClass.Car && p.SpeedKmh is not null).Select(p => p.SpeedKmh!.Value));
            }
        }
    }

    detected.AddRange(pipeline.Flush().Select(p => new DetectedPassage(p.PassedAt - first!.Value, p.Direction, p.Class, p.SpeedKmh)));
    var line = site.Line(size);
    Func<ImageMotion, TravelDirection> toDirection = m => DirectionMapping.ToDirection(line, m);
    EvaluationResult evaluation = CountEvaluator.Evaluate(truth, toDirection, detected, tolerance);

    // Detector time per second of video: below 1000 ms the hardware keeps up in real time.
    double videoSeconds = Math.Max(1e-3, (last - first!.Value).TotalSeconds);
    (long runs, long skipped) = pipeline.GateStatistics;
    string load = string.Create(CultureInfo.InvariantCulture,
        $"{processed}/{frames} frames processed ({processed / videoSeconds:0.0} fps), detector on {runs} ({(processed == 0 ? 0 : 100.0 * runs / processed):0} %), "
        + $"{(runs == 0 ? 0 : detectMilliseconds / runs):0.0} ms per run, {detectMilliseconds / videoSeconds:0} ms detector time per video second, "
        + $"wall {wall.Elapsed.TotalSeconds:0} s, {skipped} skipped by motion gate");
    if (speeds.Count > 0)
    {
        load += string.Create(CultureInfo.InvariantCulture,
            $"; car speeds: median {Percentile(speeds, 0.5):0} km/h, 10–90 % {Percentile(speeds, 0.1):0}–{Percentile(speeds, 0.9):0} km/h (n={speeds.Count})");
    }

    if (countedConfidence.Count > 0)
    {
        load += string.Create(CultureInfo.InvariantCulture,
            $"; mean track confidence of counted vehicles: 10 % {Percentile(countedConfidence, 0.1):0.00}, 25 % {Percentile(countedConfidence, 0.25):0.00}, "
            + $"median {Percentile(countedConfidence, 0.5):0.00}, 75 % {Percentile(countedConfidence, 0.75):0.00}");
    }

    return (evaluation, load, detected, toDirection);
}

// Frame-by-frame log of one section, starting 10 s earlier so the tracker has history. Coordinates are the
// bottom center of each box in frame pixels; "L" marks the counting line's x at that height.
static void Trace(string video, SiteOptions site, IObjectDetector detector, string range, TrackerSettings trackerSettings)
{
    string[] parts = range.Split('-');
    TimeSpan from = ParseTime(parts[0]);
    TimeSpan to = ParseTime(parts[1]);
    IReadOnlyList<DateTimeOffset> timestamps = FrameTimestampsFile.TryRead(video)
        ?? throw new InvalidOperationException("The video has no .frames.csv.");

    int start = 0;
    while (start < timestamps.Count - 1 && timestamps[start] - timestamps[0] < from - TimeSpan.FromSeconds(10))
    {
        start++;
    }

    using var capture = new VideoCapture(video, VideoCaptureAPIs.FFMPEG);
    capture.Set(VideoCaptureProperties.PosFrames, start);
    using var pipeline = new CountingPipeline(detector, new ByteTracker(trackerSettings), site);
    for (int index = start; index < timestamps.Count; index++)
    {
        var image = new Mat();
        if (!capture.Read(image) || image.Empty())
        {
            image.Dispose();
            break;
        }

        TimeSpan offset = timestamps[index] - timestamps[0];
        if (offset > to)
        {
            image.Dispose();
            break;
        }

        using var frame = new Frame(image, timestamps[index], index);
        FrameResult result = pipeline.Process(frame);
        if (offset < from)
        {
            continue;
        }

        string detections = string.Join(" ", result.Detections.Select(d =>
            string.Create(CultureInfo.InvariantCulture, $"{d.Class}:{d.Confidence:0.00}@{d.Box.BottomCenter.X:0},{d.Box.BottomCenter.Y:0}")));
        string tracks = string.Join(" ", result.Tracking.Active.Select(t =>
            string.Create(CultureInfo.InvariantCulture, $"#{t.TrackId}:{t.Class}@{t.Box.BottomCenter.X:0},{t.Box.BottomCenter.Y:0}")));
        string removed = result.Tracking.RemovedTrackIds.Count > 0 ? $" removed {string.Join(",", result.Tracking.RemovedTrackIds)}" : "";
        string passages = string.Join(" ", result.Passages.Select(p => $"PASSAGE #{p.TrackId} {p.Direction} {p.Class}"));
        Console.WriteLine($"{(int)offset.TotalMinutes:00}:{offset.Seconds:00}.{offset.Milliseconds / 10:00} D[{detections}] T[{tracks}]{removed} {passages}");
    }

    foreach (VehiclePassageEvent p in pipeline.Flush())
    {
        Console.WriteLine($"flush PASSAGE #{p.TrackId} {p.Direction} {p.Class}");
    }

    static TimeSpan ParseTime(string text)
    {
        string[] mmss = text.Split(':');
        return TimeSpan.FromMinutes(int.Parse(mmss[0], CultureInfo.InvariantCulture))
            + TimeSpan.FromSeconds(double.Parse(mmss[1], CultureInfo.InvariantCulture));
    }
}

// Missed and extra passages (to look them up in the video) and the class confusion of matched ones.
static float Percentile(List<float> values, double p)
{
    List<float> sorted = [.. values.Order()];
    return sorted[(int)Math.Clamp(Math.Round(p * (sorted.Count - 1)), 0, sorted.Count - 1)];
}

static void PrintDetails(EvaluationResult result)
{
    static string Time(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}";

    if (result.MissedEntries.Count > 0)
    {
        Console.WriteLine("  missed by annotated class: " + string.Join(", ", result.MissedEntries
            .GroupBy(e => e.Class?.ToString() ?? "?").OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
        foreach (GroundTruthEntry e in result.MissedEntries)
        {
            Console.WriteLine($"    missed {Time(e.Offset)} {e.Motion} {e.Class?.ToString() ?? "?"} {e.Note}");
        }
    }

    foreach (DetectedPassage p in result.SpuriousPassages)
    {
        Console.WriteLine($"    extra  {Time(p.Offset)} {p.Direction} {p.Class}");
    }

    if (result.ClassConfusion.Count > 0)
    {
        Console.WriteLine("  class (annotated -> counted): " + string.Join(", ", result.ClassConfusion
            .OrderBy(c => c.Key.Truth).ThenByDescending(c => c.Value).Select(c => $"{c.Key.Truth}->{c.Key.Detected} {c.Value}")));
    }
}

static string Format(EvaluationResult r) => string.Create(
    CultureInfo.InvariantCulture,
    $"truth {r.TruthCount,4}  counted {r.DetectedCount,4}  ok {r.Matched,4}  missed {r.Missed,3}  extra {r.Spurious,3}  wrong dir {r.DirectionErrors,3}  accuracy {r.Accuracy:P1}");

static (string[] Tags, (float Top, float Bottom)? Band) ReadMetadata(string video)
{
    string path = video + ".json";
    if (!File.Exists(path))
    {
        return ([], null);
    }

    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
    JsonElement root = document.RootElement;
    string[] tags = root.TryGetProperty("Tags", out JsonElement t) ? [.. t.EnumerateArray().Select(e => e.GetString()!)] : [];
    (float, float)? band = root.TryGetProperty("Band", out JsonElement b) && b.ValueKind == JsonValueKind.Object
        ? ((float)b.GetProperty("Top").GetDouble(), (float)b.GetProperty("Bottom").GetDouble())
        : !root.TryGetProperty("Band", out _) && root.TryGetProperty("RecordedBy", out JsonElement by) && by.GetString() == "service"
            ? RecordingBandFromSnapshot(video)
            : null;
    return (tags, band);
}

// Recordings of the service before 0.5.1 lack the band in their metadata, although the video holds only those rows.
// The configuration snapshot taken with the recording has the recording settings; they apply if recording was enabled
// and set a band. Without this the full-frame geometry lands about 150 px too low in the video.
static (float Top, float Bottom)? RecordingBandFromSnapshot(string video)
{
    string snapshot = video + ".site.json";
    if (!File.Exists(snapshot))
    {
        return null;
    }

    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(snapshot));
    JsonElement root = document.RootElement.TryGetProperty("TrafficCounter", out JsonElement tc) ? tc : document.RootElement;
    return root.TryGetProperty("Recording", out JsonElement recording)
        && recording.TryGetProperty("BandTop", out JsonElement top) && top.ValueKind == JsonValueKind.Number
        && recording.TryGetProperty("BandBottom", out JsonElement bottom) && bottom.ValueKind == JsonValueKind.Number
        && (!recording.TryGetProperty("Enabled", out JsonElement enabled) || enabled.ValueKind != JsonValueKind.False)
        ? ((float)top.GetDouble(), (float)bottom.GetDouble())
        : null;
}

// Speedometer calibration of the test vehicle (private: belongs to the user's car), e.g.
// "SpeedTest": { "SpeedometerFactor": 0.987, "SpeedometerOffsetKmh": -1.6 }.
static SpeedometerCalibration ReadSpeedometer(string configPath)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
    if (!document.RootElement.TryGetProperty("TrafficCounter", out JsonElement root) || !root.TryGetProperty("SpeedTest", out JsonElement test))
    {
        return SpeedometerCalibration.None;
    }

    double factor = test.TryGetProperty("SpeedometerFactor", out JsonElement f) ? f.GetDouble() : 1;
    double offset = test.TryGetProperty("SpeedometerOffsetKmh", out JsonElement o) ? o.GetDouble() : 0;
    return new SpeedometerCalibration(factor, offset);
}

static YoloDetectorOptions ReadDetectorOptions(string configPath)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
    return document.RootElement.TryGetProperty("TrafficCounter", out JsonElement root) && root.TryGetProperty("Detector", out JsonElement detector)
        ? detector.Deserialize<YoloDetectorOptions>(new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }) ?? new YoloDetectorOptions()
        : new YoloDetectorOptions();
}
