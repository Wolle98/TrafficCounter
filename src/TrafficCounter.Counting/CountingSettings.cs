using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Counting;

/// <summary>Parameters of the counting engine, in pixel coordinates of the processed frame.</summary>
/// <param name="Line">
/// Counting line across the road. May extend over the sidewalks so that cyclists there are counted, too (ADR-0020).
/// </param>
/// <param name="HysteresisPixels">
/// Distance a track's reference point must reach on each side of the line before a side counts as established.
/// Suppresses jitter of boxes lingering on the line.
/// </param>
/// <param name="SegmentMargin">
/// How far beyond the segment ends (as a fraction of the line length) a crossing still counts.
/// </param>
/// <param name="EmitDistancePixels">
/// Distance beyond the line a track must reach before its passage is emitted while the track is still alive.
/// Vehicles waiting on the line are therefore not emitted until they move on (ADR-0020).
/// Tracks that end earlier are emitted when they end, provided they ended on the far side.
/// A generous value costs only latency (the timestamp is always the crossing time) but protects against
/// vehicles that back up after crossing; 300 px is about 2.5–3 m at the planned camera distance.
/// </param>
/// <param name="MinTrackFrames">Tracks observed in fewer frames are treated as noise and never emitted.</param>
/// <param name="EmitDelaySeconds">
/// Minimum time after the crossing before a still visible track is emitted. A trailer enters the picture only when
/// its towing vehicle is already at the line and needs a moment to be recognised; emitting earlier would store
/// the towing vehicle without its trailers. Tracks that end are emitted immediately.
/// </param>
/// <param name="Trailers">Detection of trailers towed by another vehicle; null uses the defaults.</param>
/// <param name="Speed">Scale of the road for speed estimates (stage 5); null: no speeds.</param>
/// <param name="DuplicateSeconds">
/// A passage of a motor vehicle crossing within this time of another one in the same direction and lane is the same
/// vehicle split into two tracks (a long bus close to the camera); it is not counted again. Two real vehicles in one
/// lane cannot be that close: at 30 km/h, 0.4 s are 3.3 m. Two-wheelers are exempt, they ride side by side.
/// </param>
/// <param name="DuplicateLineTolerance">Difference in position along the line (0..1) that still counts as one lane.</param>
/// <param name="StandingSeconds">
/// A track that stayed this long within <paramref name="StandingRadiusRatio"/> of one point was standing; when it moves
/// off, its class votes, first position and speed samples start over (ADR-0052). Otherwise a vehicle driving past a
/// parked one takes over its track together with minutes of votes for the parked vehicle's class, and a vehicle
/// that waited gets the waiting time in its speed. Only before the crossing: what a track did up to the line stays
/// with it, or a pedestrian pausing behind the line would lose the speed that shows it is no vehicle. 0 switches
/// this off.
/// </param>
/// <param name="StandingRadiusRatio">Radius around the point, in box heights (so it fits near and far lanes).</param>
/// <param name="WeighVotesByWidth">
/// The class of a counted track is decided with each frame's vote weighted by its box width relative to the widest
/// box of the track: boxes cut off at the image edge or seen at a slant count less than the full side view
/// (ADR-0052). false: every frame counts the same.
/// </param>
/// <param name="MinEndBeyondPixels">
/// A track that ends shortly after crossing is only counted if it got at least this far beyond the line; it keeps
/// machines rolling a little over the line at a building site from being counted. 0: any crossing counts.
/// </param>
public sealed record CountingSettings(
    LineSegment Line,
    float HysteresisPixels = 8f,
    float SegmentMargin = 0.1f,
    float EmitDistancePixels = 300f,
    int MinTrackFrames = 3,
    float EmitDelaySeconds = 3f,
    TrailerSettings? Trailers = null,
    SpeedSettings? Speed = null,
    float DuplicateSeconds = 0.4f,
    float DuplicateLineTolerance = 0.08f,
    float StandingSeconds = 2f,
    float StandingRadiusRatio = 0.25f,
    float MinEndBeyondPixels = 0f,
    bool WeighVotesByWidth = true);

/// <summary>
/// Speed from the ground point (box bottom center) of a track near the counting line (ADR-0015): a line is fitted
/// through its horizontal position over the frame timestamps, so single jittery boxes hardly matter. Vehicles move
/// across the image, so the horizontal image speed times the scale of the road at the vehicle's lane is its speed.
/// The scale differs per lane (perspective); it is given at both kerbs and interpolated by the position of the
/// ground point along the counting line.
/// </summary>
/// <param name="FarMetersPerPixel">Meters per horizontal pixel at the far kerb.</param>
/// <param name="NearMetersPerPixel">Meters per horizontal pixel at the near kerb.</param>
/// <param name="FarPosition">Far kerb as position along the counting line (0..1).</param>
/// <param name="NearPosition">Near kerb as position along the counting line (0..1).</param>
/// <param name="ZonePixels">Only positions at most this far from the line are used (the view is least oblique there).</param>
/// <param name="MinSamples">Fewer positions in the zone give no speed.</param>
/// <param name="MinSeconds">Shorter observation in the zone gives no speed.</param>
/// <param name="StopSeconds">
/// A vehicle that moved less than <paramref name="StopMetersPerSecond"/> over this long a stretch of its time in the
/// zone stopped there: the drive was not free (ADR-0020), and a fit through the stop would give neither the speed at
/// the line nor the speed of the drive, so it gets no speed (ADR-0052).
/// </param>
/// <param name="StopMetersPerSecond">Mean speed below which such a stretch counts as a stop (1 m/s = 3.6 km/h).</param>
public sealed record SpeedSettings(
    float FarMetersPerPixel,
    float NearMetersPerPixel,
    float FarPosition,
    float NearPosition,
    float ZonePixels = 400f,
    int MinSamples = 5,
    float MinSeconds = 0.4f,
    float StopSeconds = 1f,
    float StopMetersPerSecond = 1f)
{
    /// <summary>Scale at a position along the counting line, linear between the kerbs (and beyond, for sidewalks).</summary>
    /// <remarks>
    /// The scale is proportional to the distance from the camera, whose reciprocal changes linearly with the image
    /// row for points on the road plane; so pixels per meter, not meters per pixel, are interpolated (ADR-0034).
    /// </remarks>
    public float MetersPerPixelAt(float linePosition)
    {
        if (Math.Abs(NearPosition - FarPosition) < 1e-6f)
        {
            return 2 / ((1 / FarMetersPerPixel) + (1 / NearMetersPerPixel));
        }

        float t = (linePosition - FarPosition) / (NearPosition - FarPosition);
        float pixelsPerMeter = (1 / FarMetersPerPixel) + (t * ((1 / NearMetersPerPixel) - (1 / FarMetersPerPixel)));
        return pixelsPerMeter > 1e-3f ? 1 / pixelsPerMeter : 1e3f;
    }

    /// <summary>
    /// Speed in km/h from positions (seconds, horizontal pixel, position along the line), or null with too few or
    /// after a stop in the zone. Least-squares slope of the pixel position over time, times the scale at the median
    /// lane position.
    /// </summary>
    public float? Estimate(IReadOnlyList<(double Seconds, float X, float LinePosition)> samples) =>
        Fit(samples) is (float kmh, double metersPerPixel) && !StoppedIn(samples, metersPerPixel) ? kmh : null;

    /// <summary>
    /// The same fit without the stop rule: the mean speed over the zone including stops. Not a driving speed, but
    /// enough to tell a pedestrian from a vehicle (ADR-0050).
    /// </summary>
    public float? MeanSpeed(IReadOnlyList<(double Seconds, float X, float LinePosition)> samples) => Fit(samples)?.Kmh;

    private (float Kmh, double MetersPerPixel)? Fit(IReadOnlyList<(double Seconds, float X, float LinePosition)> samples)
    {
        if (samples.Count < MinSamples || samples[^1].Seconds - samples[0].Seconds < MinSeconds)
        {
            return null;
        }

        double meanT = samples.Average(s => s.Seconds);
        double meanX = samples.Average(s => s.X);
        double covariance = samples.Sum(s => (s.Seconds - meanT) * (s.X - meanX));
        double variance = samples.Sum(s => (s.Seconds - meanT) * (s.Seconds - meanT));
        if (variance <= 0)
        {
            return null;
        }

        List<float> positions = [.. samples.Select(s => s.LinePosition).Order()];
        double metersPerPixel = MetersPerPixelAt(positions[positions.Count / 2]);
        double metersPerSecond = Math.Abs(covariance / variance) * metersPerPixel;
        return ((float)(metersPerSecond * 3.6), metersPerPixel);
    }

    /// <summary>True if some stretch of at least <see cref="StopSeconds"/> was driven slower than <see cref="StopMetersPerSecond"/>.</summary>
    private bool StoppedIn(IReadOnlyList<(double Seconds, float X, float LinePosition)> samples, double metersPerPixel)
    {
        int end = 0;
        for (int start = 0; start < samples.Count; start++)
        {
            while (end < samples.Count && samples[end].Seconds - samples[start].Seconds < StopSeconds)
            {
                end++;
            }

            if (end == samples.Count)
            {
                return false;
            }

            double seconds = samples[end].Seconds - samples[start].Seconds;
            if (Math.Abs(samples[end].X - samples[start].X) * metersPerPixel / seconds < StopMetersPerSecond)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// A track is a trailer – counted with its towing vehicle, not on its own (ADR-0013) – if it follows another
/// moving track in the same direction and lane, directly adjacent to it, with a nearly constant gap over several
/// frames. The constant gap distinguishes a rigid coupling from cars following closely. Lengths are relative to
/// the larger box height of the pair, i.e. roughly to vehicle size, so they need no calibration.
/// </summary>
/// <param name="MaxGapRatio">Largest horizontal gap between the boxes (drawbar), in box heights.</param>
/// <param name="MaxOverlapRatio">Largest horizontal overlap of the boxes, as a fraction of the narrower box.</param>
/// <param name="LaneToleranceRatio">Largest difference of the box bottoms (same lane), in box heights.</param>
/// <param name="MinSpeedRatio">
/// Both must move at least this fast, in box heights per second (1.5 ≈ 8 km/h); slow queues are never coupled.
/// </param>
/// <param name="MaxGapVariationRatio">Largest variation of the gap while coupled, in box heights.</param>
/// <param name="MinCoupledSeconds">
/// How long the coupling must be seen with a constant gap before the track counts as a trailer. Two cars following
/// closely change their gap within this time; a trailer does not.
/// </param>
/// <param name="MaxInterruptionSeconds">
/// A coupling must be seen without interruption: a candidate not confirmed in any frame for longer than this
/// starts over, so two short, separate phases never add up. Shorter gaps (a missed detection) are bridged. A
/// confirmed trailer whose gap no longer fits for <paramref name="MinCoupledSeconds"/> is released again, as long as
/// its towing vehicle has not been counted; otherwise a separate vehicle would stay wrongly coupled and never be counted.
/// </param>
public sealed record TrailerSettings(
    float MaxGapRatio = 1.0f,
    float MaxOverlapRatio = 0.3f,
    float LaneToleranceRatio = 0.35f,
    float MinSpeedRatio = 1.5f,
    float MaxGapVariationRatio = 0.25f,
    float MinCoupledSeconds = 0.6f,
    float MaxInterruptionSeconds = 0.2f)
{
    public static TrailerSettings Default { get; } = new();
}
