using System.Diagnostics;
using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;
using TrafficCounter.Core.Operations;
using TrafficCounter.Counting;
using TrafficCounter.Vision;

namespace TrafficCounter.Pipeline;

/// <summary>Everything the pipeline produced for one frame.</summary>
/// <param name="TrailerTrackIds">Active tracks recognised as trailers of another vehicle (not counted themselves).</param>
/// <param name="DetectorRan">False if the motion gate skipped detection, tracking and counting for this frame.</param>
/// <param name="Uncounted">Tracks that ended in this frame without being counted (to explain missed vehicles).</param>
public sealed record FrameResult(
    Rect Band,
    LineSegment Line,
    IReadOnlyList<Detection> Detections,
    TrackingResult Tracking,
    IReadOnlyList<VehiclePassageEvent> Passages,
    TimeSpan DetectionTime,
    IReadOnlySet<int>? TrailerTrackIds = null,
    bool DetectorRan = true,
    IReadOnlyList<UncountedTrack>? Uncounted = null);

/// <summary>
/// Detection → tracking → counting for one camera. Geometry is derived from the first frame and re-derived if
/// the frame size changes. <see cref="ProcessingOptions"/> reduce the load (ADR-0029). Not thread-safe; call from
/// one thread.
/// </summary>
public sealed class CountingPipeline(IObjectDetector detector, ITracker tracker, SiteOptions site, ProcessingOptions? processing = null)
    : IDisposable
{
    private readonly ProcessingOptions _processing = processing ?? new ProcessingOptions();
    private readonly MotionGate _gate = new((processing ?? new ProcessingOptions()).MotionGate);
    private DateTimeOffset? _nextDue;
    private List<Rect> _ignoreInBand = [];
    private Size _frameSize;
    private Rect _band;
    private LineSegment _line;
    private CountingEngine? _counter;
    private readonly LightingClassifier _lighting = new(site.Latitude, site.Longitude);
    private readonly ImageEnhancer _enhancer = new((processing ?? new ProcessingOptions()).Enhancement);

    /// <summary>Number of times the frame size changed after the first frame (e.g. camera mode switch).</summary>
    public int GeometryResets { get; private set; }

    /// <summary>Frames on which the detector ran, and on which the motion gate skipped it.</summary>
    public (long Detected, long Skipped) GateStatistics { get; private set; }

    /// <summary>
    /// False if the frame is to be dropped because of <see cref="ProcessingOptions.MaxFps"/>; the caller then does not
    /// call <see cref="Process"/>. Frames are taken on a schedule of 1/MaxFps rather than with a minimum gap, so the
    /// average rate is met whatever the camera delivers: from 20 fps at night three of four frames give 15 fps (a
    /// minimum gap would take every second one, i.e. 10 fps), from 30 fps every second one.
    /// </summary>
    public bool ShouldProcess(DateTimeOffset timestamp)
    {
        if (_processing.MaxFps <= 0)
        {
            return true;
        }

        TimeSpan interval = TimeSpan.FromSeconds(1 / _processing.MaxFps);
        if (_nextDue is DateTimeOffset due && timestamp < due - (interval * 0.2))
        {
            return false; // the tolerance absorbs jitter of the camera timestamps
        }

        // Next slot after this one; after a gap (camera hiccup) restart the schedule instead of catching up.
        _nextDue = _nextDue is DateTimeOffset previous && timestamp - previous < interval ? previous + interval : timestamp + interval;
        return true;
    }

    public FrameResult Process(Frame frame)
    {
        List<VehiclePassageEvent> passages = [];
        Size size = frame.Image.Size();
        if (_counter is null || size != _frameSize)
        {
            if (_counter is not null)
            {
                passages.AddRange(Counted(_counter.Flush()));
                GeometryResets++;
            }

            _frameSize = size;
            _band = site.BandRect(size);
            tracker.SetHorizontalExtent(_band.Left, _band.Right);
            _line = site.Line(size);
            _counter = new CountingEngine(site.CountingSettings(size));
            _ignoreInBand = [.. site.IgnoreZones.Select(z => new Rect(
                (int)(z.X * size.Width) - _band.X, (int)(z.Y * size.Height) - _band.Y, (int)(z.Width * size.Width), (int)(z.Height * size.Height)))];
            _gate.Reset();
        }

        if (_processing.MotionGate.Enabled && !_counter.HasPendingPassages)
        {
            using Mat band = new(frame.Image, _band);
            if (!_gate.ShouldDetect(band, frame.Timestamp, _ignoreInBand))
            {
                GateStatistics = (GateStatistics.Detected, GateStatistics.Skipped + 1);
                return new FrameResult(_band, _line, [], new TrackingResult(frame.Timestamp, [], []), passages, TimeSpan.Zero, null, DetectorRan: false);
            }
        }

        GateStatistics = (GateStatistics.Detected + 1, GateStatistics.Skipped);
        LightingCondition lighting = _lighting.Classify(frame.Timestamp);
        var watch = Stopwatch.StartNew();
        Mat input = _processing.Enhancement.AppliesTo(lighting) ? _enhancer.Enhance(frame.Image, _band) : frame.Image;
        IReadOnlyList<Detection> detections = detector.Detect(input, _band);
        watch.Stop();
        if (site.IgnoreZones.Count > 0)
        {
            // Parked vehicles in driveways etc. (ADR-0025): judged by the ground point, like the counting.
            detections = [.. detections.Where(d => !site.IsIgnored(d.Box.BottomCenter.X / size.Width, d.Box.BottomCenter.Y / size.Height))];
        }

        TrackingResult tracking = tracker.Update(detections, frame.Timestamp, lighting);
        passages.AddRange(Counted(_counter.Process(tracking)));
        return new FrameResult(
            _band, _line, detections, tracking, passages, watch.Elapsed, _counter.TrailerTrackIds.ToHashSet(), Uncounted: [.. _counter.Uncounted]);
    }

    /// <summary>Emits passages still waiting for their track to end, e.g. on shutdown.</summary>
    public IReadOnlyList<VehiclePassageEvent> Flush() => [.. Counted(_counter?.Flush() ?? [])];

    /// <summary>Passages that are vehicles: prams and the like on the sidewalk, taken for a motorcycle, are dropped.</summary>
    private IEnumerable<VehiclePassageEvent> Counted(IEnumerable<VehiclePassageEvent> passages) =>
        passages.Where(p => !site.IsSidewalkPseudoVehicle(p.Class, p.SpeedKmh, p.LinePosition));

    public void Dispose()
    {
        _gate.Dispose();
        _enhancer.Dispose();
    }
}
