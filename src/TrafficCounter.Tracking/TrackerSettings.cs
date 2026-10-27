namespace TrafficCounter.Tracking;

/// <summary>Parameters of <see cref="ByteTracker"/>. Defaults follow ByteTrack, adapted per ADR-0020.</summary>
public sealed record TrackerSettings
{
    /// <summary>Detections at or above this confidence are "high" (first association, may start tracks).</summary>
    public float HighConfidence { get; init; } = 0.5f;

    /// <summary>Detections below this are ignored; between low and high they only continue existing tracks.</summary>
    public float LowConfidence { get; init; } = 0.1f;

    /// <summary>Minimum confidence for a detection to start a new track.</summary>
    public float NewTrackConfidence { get; init; } = 0.6f;

    /// <summary>
    /// <see cref="HighConfidence"/> at night (ADR-0028). In the dark the model sees vehicles mostly by their lights
    /// and stays unsure until they are past the line; the day thresholds then start the track too late.
    /// </summary>
    public float NightHighConfidence { get; init; } = 0.2f;

    /// <summary><see cref="NewTrackConfidence"/> at night (ADR-0028).</summary>
    public float NightNewTrackConfidence { get; init; } = 0.25f;

    /// <summary>Uses the night thresholds also at dusk (civil twilight); off: dusk keeps the day values (ADR-0028).</summary>
    public bool NightThresholdsAtDusk { get; init; }

    /// <summary>
    /// Optional lower <see cref="HighConfidence"/> for bicycles and motorcycles: the COCO model sees cyclists and
    /// e-scooters from the side often with only 0.3–0.5. Null: same as for all classes.
    /// </summary>
    public float? TwoWheelerHighConfidence { get; init; }

    /// <summary>Optional lower <see cref="NewTrackConfidence"/> for bicycles and motorcycles; null: same as for all.</summary>
    public float? TwoWheelerNewTrackConfidence { get; init; }

    /// <summary>Maximum 1 - IoU for the first association (high detections vs. confirmed tracks).</summary>
    public double FirstMatchMaxCost { get; init; } = 0.8;

    /// <summary>Maximum 1 - IoU for the second association (low detections vs. tracks still tracked).</summary>
    public double SecondMatchMaxCost { get; init; } = 0.5;

    /// <summary>Maximum 1 - IoU for confirming a tentative track with its second detection.</summary>
    public double TentativeMatchMaxCost { get; init; } = 0.7;

    /// <summary>
    /// Stitching: maximum distance between a detection and the path of a lost track (from its last observed
    /// position to its predicted one), in box sizes.
    /// </summary>
    public double StitchMaxDistance { get; init; } = 0.75;

    /// <summary>
    /// Stitching: besides the filter prediction, the path is also extended forward along the track's average
    /// velocity of the last seconds, by this many times the distance it would have covered during the gap. Covers
    /// vehicles that accelerate or cross while hidden; the average ignores the misleading last frames in which the
    /// box shrank behind the occluder.
    /// </summary>
    public double StitchForwardFactor { get; init; } = 2.0;

    /// <summary>
    /// Stitching: a lost track whose last box was within this many box widths of the left or right image edge,
    /// moving towards it, has left the image and is not stitched to a vehicle entering there.
    /// </summary>
    public double StitchEdgeMargin { get; init; } = 0.25;

    /// <summary>Stitching: allowed ratio between detection and track box height (partial occlusion changes it).</summary>
    public double StitchMaxSizeRatio { get; init; } = 2.5;

    /// <summary>How long a fast track may stay lost before it is removed.</summary>
    public TimeSpan LostTimeoutMoving { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long a slow or stationary track may stay lost (e.g. a waiting car hidden by oncoming traffic).</summary>
    public TimeSpan LostTimeoutStationary { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>Speed (box heights per second) below which a track counts as slow for the lost timeout.</summary>
    public double SlowSpeed { get; init; } = 1.0;
}
