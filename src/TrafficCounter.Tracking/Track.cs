using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Tracking;

internal enum TrackState
{
    Tracked,
    Lost,
    Removed,
}

/// <summary>One tracked object with its motion model.</summary>
internal sealed class Track
{
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MinHistory = TimeSpan.FromSeconds(0.3);

    private readonly Queue<(DateTimeOffset Time, BoundingBox Box)> _history = new();
    private KalmanBoxFilter _filter;
    private DateTimeOffset _predictedAt;
    private (float Width, float Height)? _typicalSize;

    public Track(int id, Detection detection, DateTimeOffset timestamp)
    {
        Id = id;
        _filter = new KalmanBoxFilter(detection.Box);
        _predictedAt = timestamp;
        Observe(detection, timestamp);
    }

    public int Id { get; }

    public TrackState State { get; private set; } = TrackState.Tracked;

    /// <summary>Confirmed tracks have been matched in at least two frames; only they are reported.</summary>
    public bool IsConfirmed => Hits >= 2;

    public int Hits { get; private set; }

    public VehicleClass Class { get; private set; }

    public float Confidence { get; private set; }

    public DateTimeOffset LastSeen { get; private set; }

    public Point2 LastObservedCenter { get; private set; }

    public BoundingBox Box => _filter.Box;

    /// <summary>Box of the last detection (not the prediction).</summary>
    public BoundingBox LastObservedBox => _history.Count > 0 ? _history.Last().Box : _filter.Box;

    public double SpeedInHeightsPerSecond => _filter.Velocity.Length / Math.Max(1f, _filter.Box.Height);

    /// <summary>
    /// Average velocity of the box center over the last two seconds of observations (pixels per second), or null
    /// with too little history. Unlike the filter velocity it is hardly affected by the last few frames, in which
    /// a box shrinking behind an occluder moves its center the wrong way.
    /// </summary>
    public Point2? AverageVelocity
    {
        get
        {
            if (_history.Count < 2)
            {
                return null;
            }

            (DateTimeOffset firstTime, BoundingBox first) = _history.Peek();
            double seconds = (LastSeen - firstTime).TotalSeconds;
            return seconds < MinHistory.TotalSeconds ? null : (LastObservedCenter - first.Center) * (float)(1 / seconds);
        }
    }

    /// <summary>
    /// Median width and height of the boxes observed in the last two seconds: the size of the vehicle, not of the
    /// strip still visible in the last frames before an occlusion. Cached until the next observation, as the
    /// tracker asks for it for every lost track in every frame.
    /// </summary>
    public (float Width, float Height) TypicalSize
    {
        get
        {
            if (_history.Count == 0)
            {
                return (Box.Width, Box.Height);
            }

            if (_typicalSize is null)
            {
                List<float> widths = [.. _history.Select(h => h.Box.Width).Order()];
                List<float> heights = [.. _history.Select(h => h.Box.Height).Order()];
                _typicalSize = (widths[widths.Count / 2], heights[heights.Count / 2]);
            }

            return _typicalSize.Value;
        }
    }

    public void Predict(DateTimeOffset timestamp)
    {
        _filter.Predict((timestamp - _predictedAt).TotalSeconds);
        _predictedAt = timestamp;
    }

    public void Update(Detection detection, DateTimeOffset timestamp)
    {
        _filter.Update(detection.Box);
        Observe(detection, timestamp);
    }

    /// <summary>
    /// Re-attaches a lost track. The motion model is restarted with the average velocity over the gap, because the
    /// prediction through a long occlusion is unreliable (the vehicle may have stopped).
    /// </summary>
    public void Stitch(Detection detection, DateTimeOffset timestamp)
    {
        double gap = Math.Max(1e-3, (timestamp - LastSeen).TotalSeconds);
        Point2 velocity = (detection.Box.Center - LastObservedCenter) * (float)(1 / gap);
        _filter = new KalmanBoxFilter(detection.Box, velocity);
        _predictedAt = timestamp;
        Observe(detection, timestamp);
    }

    public void MarkLost() => State = TrackState.Lost;

    public void MarkRemoved() => State = TrackState.Removed;

    private void Observe(Detection detection, DateTimeOffset timestamp)
    {
        Hits++;
        State = TrackState.Tracked;
        Class = detection.Class;
        Confidence = detection.Confidence;
        LastSeen = timestamp;
        LastObservedCenter = detection.Box.Center;
        _history.Enqueue((timestamp, detection.Box));
        _typicalSize = null;
        while (_history.Count > 2 && timestamp - _history.Peek().Time > HistoryWindow)
        {
            _history.Dequeue();
        }
    }
}
