using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Core;

/// <summary>A single detection, already mapped to a target vehicle class.</summary>
public sealed record Detection(BoundingBox Box, VehicleClass Class, float Confidence);

/// <summary>State of one tracked object in the current frame.</summary>
public sealed record TrackedObject(int TrackId, BoundingBox Box, VehicleClass Class, float Confidence);

/// <summary>Output of the tracker for one frame.</summary>
/// <param name="Timestamp">Capture time of the frame.</param>
/// <param name="Active">Tracks updated in this frame.</param>
/// <param name="RemovedTrackIds">Tracks that ended in this frame and will not appear again.</param>
public sealed record TrackingResult(
    DateTimeOffset Timestamp,
    IReadOnlyList<TrackedObject> Active,
    IReadOnlyList<int> RemovedTrackIds);

public interface ITracker
{
    /// <param name="lighting">Lighting of the frame; trackers may use lower confidence thresholds at night (ADR-0028).</param>
    TrackingResult Update(
        IReadOnlyList<Detection> detections, DateTimeOffset timestamp, Operations.LightingCondition lighting = Operations.LightingCondition.Unknown);

    /// <summary>
    /// Left and right edge of the analysed area in pixels, where vehicles enter and leave. Without it a tracker may
    /// infer them from the detections seen so far.
    /// </summary>
    void SetHorizontalExtent(float left, float right)
    {
    }
}
