using TrafficCounter.Core;
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
/// One position of a track for the speed estimate: the box of a processed frame near the counting line.
/// </summary>
/// <param name="Seconds">Time since the first position.</param>
/// <param name="X">Bottom center x (the ground point, ADR-0015).</param>
/// <param name="LinePosition">Position of the ground point along the counting line (0..1).</param>
/// <param name="Distance">Distance of the ground point from the line in pixels (selects the zone).</param>
/// <param name="Left">Left box edge.</param>
/// <param name="Right">Right box edge.</param>
/// <param name="Top">Top box edge.</param>
/// <param name="Bottom">Bottom box edge (the ground point's row).</param>
/// <param name="Capture">Capture sequence number of the frame at the source; null if unknown.</param>
public readonly record struct SpeedSample(
    double Seconds, float X, float LinePosition, float Distance = 0, float Left = 0, float Right = 0, float Top = 0, float Bottom = 0,
    long? Capture = null);

/// <summary>
/// Speed of a track near the counting line (ADR-0015, ADR-0056): a line is fitted through its positions over the
/// frame timestamps, so single jittery boxes hardly matter.
/// <para>
/// With a <see cref="CameraModel"/> the positions are meters along the road: the lane comes from the ground point
/// where it is the bottom of the near side (close to the principal point), and each box edge that belongs to the
/// vehicle's camera-near vertical edge is put into that lane, with the edge rising up to the box top. Left of the
/// principal point that is the box's left edge, right of it the right edge, around it both; one common slope with one
/// offset per edge needs no vehicle length. This holds for long and tall vehicles too, whose box centre does not move
/// with the vehicle (a bus cut off at the image border).
/// </para>
/// <para>
/// Without a camera model the horizontal image speed of the ground point times the scale of the road at the
/// vehicle's lane is its speed; the scale is given at both kerbs and interpolated by the position along the line.
/// </para>
/// </summary>
/// <param name="FarMetersPerPixel">Meters per horizontal pixel at the far kerb (without a camera model).</param>
/// <param name="NearMetersPerPixel">Meters per horizontal pixel at the near kerb (without a camera model).</param>
/// <param name="FarPosition">Far kerb as position along the counting line (0..1).</param>
/// <param name="NearPosition">Near kerb as position along the counting line (0..1).</param>
/// <param name="ZonePixels">Positions at most this far from the line are used (the view is least oblique there).</param>
/// <param name="MinSamples">
/// Fewer positions give no speed: fits through five or six positions were up to 10 % off (ADR-0056). Fast vehicles
/// have few positions in the zone; for them it grows (<paramref name="WantedSamples"/>).
/// </param>
/// <param name="MinSeconds">Shorter observation in the zone gives no speed.</param>
/// <param name="StopSeconds">
/// A vehicle that moved less than <paramref name="StopMetersPerSecond"/> over this long a stretch of its time in the
/// zone stopped there: the drive was not free (ADR-0020), and a fit through the stop would give neither the speed at
/// the line nor the speed of the drive, so it gets no speed (ADR-0052).
/// </param>
/// <param name="StopMetersPerSecond">Mean speed below which such a stretch counts as a stop (1 m/s = 3.6 km/h).</param>
/// <param name="MaxZonePixels">
/// With fewer than <paramref name="WantedSamples"/> positions in the zone, it grows in steps of
/// <see cref="ZoneStepPixels"/> up to this distance from the line.
/// </param>
/// <param name="Camera">Camera model for the processed frame; null: linear scale between the kerbs.</param>
/// <param name="WantedSamples">
/// The zone grows until it has this many positions: vehicles drive slightly diagonally, so with few positions the
/// speed depends on where in the zone they lie (ADR-0062). At least <paramref name="MinSamples"/>.
/// </param>
/// <param name="MaxCaptureRateDeviation">
/// While the vehicle was in the zone, the source must have delivered frames at its normal rate (<see cref="CaptureRate"/>)
/// within this share; otherwise it gets no speed. A stalled stream delivers frames late and then in a rush, their
/// timestamps (arrival times) no longer match the moments they show, and the speed comes out far too low or too high
/// (ADR-0063). 0: no check.
/// </param>
public sealed record SpeedSettings(
    float FarMetersPerPixel,
    float NearMetersPerPixel,
    float FarPosition,
    float NearPosition,
    float ZonePixels = 400f,
    int MinSamples = 8,
    float MinSeconds = 0.4f,
    float StopSeconds = 1f,
    float StopMetersPerSecond = 1f,
    float MaxZonePixels = 800f,
    CameraModel? Camera = null,
    int WantedSamples = 12,
    float MaxCaptureRateDeviation = 0.15f)
{
    /// <summary>Step by which the zone grows for fast vehicles.</summary>
    public const float ZoneStepPixels = 50f;

    /// <summary>Box edges closer than this to the image border are cut off and not used (camera model).</summary>
    public const float BorderMarginPixels = 30f;

    /// <summary>Ground points this close to the principal point's column give the lane (camera model).</summary>
    public const float LaneBandPixels = 150f;

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

    /// <summary>Speed in km/h, or null with too few positions or after a stop in the zone.</summary>
    public float? Estimate(IReadOnlyList<SpeedSample> samples, VehicleClass vehicleClass = VehicleClass.Car) =>
        Fit(samples, vehicleClass) is (float kmh, List<(double Seconds, double Meters)> track) && !StoppedIn(track) ? kmh : null;

    /// <summary>
    /// The same fit without the stop rule: the mean speed over the zone including stops. Not a driving speed, but
    /// enough to tell a pedestrian from a vehicle (ADR-0050).
    /// </summary>
    public float? MeanSpeed(IReadOnlyList<SpeedSample> samples, VehicleClass vehicleClass = VehicleClass.Car) =>
        Fit(samples, vehicleClass)?.Kmh;

    /// <summary>
    /// Typical width of a class in meters: the box top is the roof's far edge, this far behind the near side
    /// (camera model).
    /// </summary>
    public static double WidthOf(VehicleClass vehicleClass) => vehicleClass switch
    {
        VehicleClass.Bus => 2.55,
        VehicleClass.Truck or VehicleClass.Agricultural => 2.5,
        VehicleClass.Van => 2.0,
        VehicleClass.Motorcycle => 0.8,
        VehicleClass.Bicycle or VehicleClass.EScooter => 0.6,
        _ => 1.8,
    };

    /// <summary>The positions in the zone; with too few of them, in a zone grown up to <see cref="MaxZonePixels"/>.</summary>
    public List<SpeedSample> InZone(IReadOnlyList<SpeedSample> samples)
    {
        float max = Math.Max(ZonePixels, MaxZonePixels);
        int wanted = Math.Max(MinSamples, WantedSamples);
        for (float limit = ZonePixels; ; limit = Math.Min(limit + ZoneStepPixels, max))
        {
            List<SpeedSample> zone = [.. samples.Where(s => Math.Abs(s.Distance) <= limit)];
            if (zone.Count >= wanted || limit >= max)
            {
                return zone;
            }
        }
    }

    /// <summary>
    /// Rate at which the source delivered frames while the vehicle was in the zone, relative to its normal rate
    /// (1 = normal); null without capture sequence numbers or a normal rate.
    /// </summary>
    public static float? CaptureRateRatio(IReadOnlyList<SpeedSample> zone, double? normalPerSecond)
    {
        if (normalPerSecond is not double normal || normal <= 0 || zone.Count < 2
            || zone[0].Capture is not long first || zone[^1].Capture is not long last)
        {
            return null;
        }

        double seconds = zone[^1].Seconds - zone[0].Seconds;
        return seconds > 0 ? (float)((last - first) / seconds / normal) : null;
    }

    /// <summary>True if the ratio from <see cref="CaptureRateRatio"/> lies outside <see cref="MaxCaptureRateDeviation"/>.</summary>
    public bool CaptureRateDeviates(float? ratio) =>
        MaxCaptureRateDeviation > 0 && ratio is float r && MathF.Abs(r - 1) > MaxCaptureRateDeviation;

    /// <summary>Speed and the ground point's way along the road (for the stop rule).</summary>
    private (float Kmh, List<(double Seconds, double Meters)> Track)? Fit(IReadOnlyList<SpeedSample> all, VehicleClass vehicleClass)
    {
        List<SpeedSample> samples = InZone(all);
        if (samples.Count < MinSamples || samples[^1].Seconds - samples[0].Seconds < MinSeconds)
        {
            return null;
        }

        return Camera is { } camera ? FitOnRoad(samples, camera, WidthOf(vehicleClass)) : FitLinear(samples);
    }

    private (float Kmh, List<(double Seconds, double Meters)> Track)? FitLinear(List<SpeedSample> samples)
    {
        List<float> positions = [.. samples.Select(s => s.LinePosition).Order()];
        double metersPerPixel = MetersPerPixelAt(positions[positions.Count / 2]);
        List<(double Seconds, double Meters)> track = [.. samples.Select(s => (s.Seconds, s.X * metersPerPixel))];
        return Slope([.. track.Select(p => (p.Seconds, p.Meters, 0))]) is double metersPerSecond
            ? ((float)(Math.Abs(metersPerSecond) * 3.6), track)
            : null;
    }

    private (float Kmh, List<(double Seconds, double Meters)> Track)? FitOnRoad(List<SpeedSample> samples, CameraModel camera, double width)
    {
        // The lane: where the ground point is the bottom of the near side, i.e. close to the principal point's column.
        double principal = camera.PrincipalFrameX;
        List<SpeedSample> central = [.. samples.Where(s => Math.Abs(s.X - principal) < LaneBandPixels)];
        if (central.Count == 0)
        {
            double nearest = samples.Min(s => Math.Abs(s.X - principal));
            central = [.. samples.Where(s => Math.Abs(s.X - principal) <= nearest + 1)];
        }

        List<double> lanes = [.. central.Select(s => camera.ToRoad(s.X, s.Bottom).Across).Order()];
        double lane = lanes[lanes.Count / 2];

        List<(double Seconds, double Meters, int Edge)> edges = [];
        int used = 0;
        foreach (SpeedSample s in samples)
        {
            bool straddles = s.Left < principal && principal < s.Right;
            bool any = false;
            if (s.Left > BorderMarginPixels && (s.Right <= principal || straddles)
                && camera.AlongOfEdge(s.Left, lane, s.Top, outermostRight: false, width) is double left)
            {
                edges.Add((s.Seconds, left, 0));
                any = true;
            }

            if (s.Right < camera.FrameWidth - BorderMarginPixels && (s.Left >= principal || straddles)
                && camera.AlongOfEdge(s.Right, lane, s.Top, outermostRight: true, width) is double right)
            {
                edges.Add((s.Seconds, right, 1));
                any = true;
            }

            used += any ? 1 : 0;
        }

        // The ground points for the stop rule (a stop shows in any reference point).
        List<(double Seconds, double Meters)> track = [.. samples.Select(s => (s.Seconds, camera.ToRoad(s.X, s.Bottom).Along))];
        List<(double Seconds, double Meters, int Group)> fitted = used >= MinSamples
            ? edges
            : [.. track.Select(p => (p.Seconds, p.Meters, 0))]; // edges cut off or hidden in most frames
        return Slope(fitted) is double metersPerSecond ? ((float)(Math.Abs(metersPerSecond) * 3.6), track) : null;
    }

    /// <summary>Least-squares slope with one common slope and one offset per group.</summary>
    private static double? Slope(List<(double Seconds, double Meters, int Group)> points)
    {
        double covariance = 0, variance = 0;
        foreach (IGrouping<int, (double Seconds, double Meters, int Group)> group in points.GroupBy(p => p.Group))
        {
            double meanT = group.Average(p => p.Seconds);
            double meanM = group.Average(p => p.Meters);
            covariance += group.Sum(p => (p.Seconds - meanT) * (p.Meters - meanM));
            variance += group.Sum(p => (p.Seconds - meanT) * (p.Seconds - meanT));
        }

        return variance > 0 ? covariance / variance : null;
    }

    /// <summary>True if some stretch of at least <see cref="StopSeconds"/> was driven slower than <see cref="StopMetersPerSecond"/>.</summary>
    private bool StoppedIn(List<(double Seconds, double Meters)> track)
    {
        int end = 0;
        for (int start = 0; start < track.Count; start++)
        {
            while (end < track.Count && track[end].Seconds - track[start].Seconds < StopSeconds)
            {
                end++;
            }

            if (end == track.Count)
            {
                return false;
            }

            double seconds = track[end].Seconds - track[start].Seconds;
            if (Math.Abs(track[end].Meters - track[start].Meters) / seconds < StopMetersPerSecond)
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
