using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Counting;

/// <summary>
/// Turns tracker output into passage events: a track is counted once when its bottom-center point
/// crosses the counting line (ADR-0009, refined by ADR-0020). Trailers are counted with their towing vehicle
/// (ADR-0013, see <see cref="TrailerSettings"/>). Not thread-safe; feed it from the pipeline thread only.
/// </summary>
public sealed class CountingEngine(CountingSettings settings)
{
    private readonly Dictionary<int, TrackState> _tracks = [];
    private readonly TrailerSettings _trailers = settings.Trailers ?? TrailerSettings.Default;
    private readonly List<VehiclePassageEvent> _recent = [];

    /// <summary>
    /// Bicycles, e-scooters and motorcycles neither tow nor are trailers: riders in line keep a constant gap like a
    /// coupling and would otherwise be counted as one with a trailer.
    /// </summary>
    private static bool IsTwoWheeler(VehicleClass vehicleClass) =>
        vehicleClass is VehicleClass.Bicycle or VehicleClass.EScooter or VehicleClass.Motorcycle;

    public int ActiveTrackCount => _tracks.Count;

    /// <summary>
    /// True while a track is at the line or has crossed it without being emitted yet. A motion gate must then keep
    /// the detector running: a vehicle standing there produces no motion, and while detection is skipped the
    /// tracker would drop it (stationary timeout shorter than the longest skip) and its passage would be lost.
    /// </summary>
    public bool HasPendingPassages => _tracks.Values.Any(s => !s.Emitted
        && (s.Crossing is not null || (s.LastDistance is float d && MathF.Abs(d) <= settings.EmitDistancePixels)));

    /// <summary>Tracks currently recognised as trailers (for the debug preview).</summary>
    public IEnumerable<int> TrailerTrackIds => _tracks.Values.Where(s => s.IsTrailer).Select(s => s.TrackId);

    public IReadOnlyList<VehiclePassageEvent> Process(TrackingResult result)
    {
        List<VehiclePassageEvent> passages = [];

        foreach (TrackedObject obj in result.Active)
        {
            if (!_tracks.TryGetValue(obj.TrackId, out TrackState? state))
            {
                state = new TrackState(obj.TrackId);
                _tracks.Add(obj.TrackId, state);
            }

            Observe(state, obj, result.Timestamp);
        }

        UpdateCouplings(result.Active, result.Timestamp);

        foreach (TrackedObject obj in result.Active)
        {
            TrackState state = _tracks[obj.TrackId];
            if (IsReadyToEmit(state)
                && state.DistanceBeyondLine >= settings.EmitDistancePixels
                && (result.Timestamp - state.Crossing!.Value.At).TotalSeconds >= settings.EmitDelaySeconds)
            {
                Add(passages, Emit(state));
            }
        }

        foreach (int trackId in result.RemovedTrackIds)
        {
            if (_tracks.TryGetValue(trackId, out TrackState? state) && IsReadyToEmit(state) && state.EndedOnFarSide)
            {
                Add(passages, Emit(state));
            }

            _tracks.Remove(trackId);
        }

        return passages;
    }

    /// <summary>Emits all counted but not yet emitted passages, e.g. on shutdown, and resets the engine.</summary>
    public IReadOnlyList<VehiclePassageEvent> Flush()
    {
        List<VehiclePassageEvent> passages = [];
        foreach (TrackState state in _tracks.Values.Where(s => IsReadyToEmit(s) && s.EndedOnFarSide).ToList())
        {
            Add(passages, Emit(state));
        }

        _tracks.Clear();
        _recent.Clear();
        return passages;
    }

    /// <summary>Adds a passage unless it is a second track of a vehicle already counted (see <see cref="CountingSettings.DuplicateSeconds"/>).</summary>
    private void Add(List<VehiclePassageEvent> passages, VehiclePassageEvent passage)
    {
        _recent.RemoveAll(r => (passage.PassedAt - r.PassedAt).Duration().TotalSeconds > 10);
        bool duplicate = !IsTwoWheeler(passage.Class) && _recent.Any(r =>
            r.Direction == passage.Direction && !IsTwoWheeler(r.Class)
            && (passage.PassedAt - r.PassedAt).Duration().TotalSeconds <= settings.DuplicateSeconds
            && MathF.Abs(passage.LinePosition - r.LinePosition) <= settings.DuplicateLineTolerance);
        if (duplicate)
        {
            return;
        }

        _recent.Add(passage);
        passages.Add(passage);
    }

    private bool IsReadyToEmit(TrackState state) =>
        !state.Emitted && !state.IsTrailer && state.Crossing is not null && state.Frames >= settings.MinTrackFrames;

    private VehiclePassageEvent Emit(TrackState state)
    {
        int trailers = _tracks.Values.Count(s => s.IsTrailer && Root(s) == state.TrackId);
        return state.Emit(trailers, settings.Speed);
    }

    /// <summary>Towing vehicle at the head of a chain of trailers (tractor with two trailers).</summary>
    private int Root(TrackState trailer)
    {
        TrackState current = trailer;
        for (int i = 0; i < 10 && current.LeaderId is int leader && _tracks.TryGetValue(leader, out TrackState? next); i++)
        {
            current = next;
        }

        return current.TrackId;
    }

    private void Observe(TrackState state, TrackedObject obj, DateTimeOffset timestamp)
    {
        Point2 point = obj.Box.BottomCenter;
        float distance = settings.Line.SignedDistance(point);

        state.Frames++;
        if (state.Frames == 1)
        {
            state.FirstTimestamp = timestamp;
            state.FirstDistance = distance;
        }

        state.Vote(obj.Class, obj.Confidence);
        state.UpdateVelocity(point.X, timestamp);
        if (settings.Speed is { } speed && MathF.Abs(distance) <= speed.ZonePixels)
        {
            state.AddSpeedSample(timestamp, point.X, settings.Line.Project(point));
        }

        // Remember when the point last changed sides, interpolated between the two frames.
        if (state.LastDistance is float last && last != 0 && distance != 0 && MathF.Sign(last) != MathF.Sign(distance))
        {
            float fraction = MathF.Abs(last) / (MathF.Abs(last) + MathF.Abs(distance));
            state.LastSignChangeAt = state.LastTimestamp + ((timestamp - state.LastTimestamp) * fraction);
        }

        state.LastDistance = distance;
        state.LastTimestamp = timestamp;

        if (MathF.Abs(distance) < settings.HysteresisPixels)
        {
            return;
        }

        int side = MathF.Sign(distance);
        if (state.EstablishedSide is int previousSide && previousSide != side && state.Crossing is null)
        {
            float position = settings.Line.Project(point);
            if (position >= -settings.SegmentMargin && position <= 1 + settings.SegmentMargin)
            {
                state.Crossing = new Crossing(side, state.LastSignChangeAt ?? timestamp, position);
            }
        }

        state.EstablishedSide = side;
    }

    /// <summary>Looks for the vehicle each track directly follows and confirms rigid couplings.</summary>
    private void UpdateCouplings(IReadOnlyList<TrackedObject> active, DateTimeOffset timestamp)
    {
        foreach (TrackedObject follower in active)
        {
            TrackState followerState = _tracks[follower.TrackId];
            if (followerState.LeaderId is int confirmedLeader)
            {
                RecheckCoupling(follower, followerState, confirmedLeader, active, timestamp);
                continue;
            }

            if (IsTwoWheeler(followerState.LeadingClass))
            {
                continue;
            }

            (int LeaderId, float Gap, float Scale)? best = null;
            foreach (TrackedObject leader in active)
            {
                TrackState leaderState = _tracks[leader.TrackId];
                if (leader.TrackId != follower.TrackId
                    && leaderState.LeaderId != follower.TrackId
                    && !IsTwoWheeler(leaderState.LeadingClass)
                    && CouplingGap(follower.Box, followerState.VelocityX, leader.Box, leaderState.VelocityX) is float gap
                    && (best is null || gap < best.Value.Gap))
                {
                    best = (leader.TrackId, gap, MathF.Max(follower.Box.Height, leader.Box.Height));
                }
            }

            if (best is { } b)
            {
                followerState.Couple(b.LeaderId, b.Gap, b.Scale, timestamp, _trailers);
            }
        }
    }

    /// <summary>
    /// Releases a confirmed trailer whose gap to its towing vehicle no longer fits for longer than the allowed
    /// interruption, as long as the towing vehicle has not been counted (two cars that only drove alike for a while).
    /// A towing vehicle that has left the picture is no reason: its trailer follows it out.
    /// </summary>
    private void RecheckCoupling(TrackedObject follower, TrackState followerState, int leaderId, IReadOnlyList<TrackedObject> active, DateTimeOffset timestamp)
    {
        TrackedObject? leader = active.FirstOrDefault(a => a.TrackId == leaderId);
        if (leader is null || !_tracks.TryGetValue(leaderId, out TrackState? leaderState) || leaderState.Emitted)
        {
            return;
        }

        // The class vote can turn while the tracks are coupled: a two-wheeler is never a trailer.
        if (IsTwoWheeler(followerState.LeadingClass) || IsTwoWheeler(leaderState.LeadingClass))
        {
            followerState.Release();
            return;
        }

        float scale = MathF.Max(follower.Box.Height, leader.Box.Height);
        float? gap = CouplingGap(follower.Box, followerState.VelocityX, leader.Box, leaderState.VelocityX);
        followerState.Recheck(gap, scale, timestamp, _trailers);
    }

    /// <summary>Horizontal gap if <paramref name="follower"/> moves directly behind <paramref name="leader"/>, else null.</summary>
    private float? CouplingGap(BoundingBox follower, float followerVelocity, BoundingBox leader, float leaderVelocity)
    {
        float scale = MathF.Max(follower.Height, leader.Height);
        float minSpeed = _trailers.MinSpeedRatio * scale;
        if (MathF.Abs(followerVelocity) < minSpeed || MathF.Abs(leaderVelocity) < minSpeed
            || MathF.Sign(followerVelocity) != MathF.Sign(leaderVelocity)
            || MathF.Abs(follower.Bottom - leader.Bottom) > _trailers.LaneToleranceRatio * scale)
        {
            return null;
        }

        bool movingRight = leaderVelocity > 0;
        if (movingRight ? follower.Center.X >= leader.Center.X : follower.Center.X <= leader.Center.X)
        {
            return null; // not behind
        }

        float gap = movingRight ? leader.Left - follower.Right : follower.Left - leader.Right;
        float maxOverlap = _trailers.MaxOverlapRatio * MathF.Min(follower.Width, leader.Width);
        return gap >= -maxOverlap && gap <= _trailers.MaxGapRatio * scale ? gap : null;
    }

    private readonly record struct Crossing(int Side, DateTimeOffset At, float LinePosition)
    {
        // Moving onto the right-hand (positive) side means direction A.
        public TravelDirection Direction => Side > 0 ? TravelDirection.A : TravelDirection.B;
    }

    private sealed class TrackState(int trackId)
    {
        private const float VelocitySmoothing = 0.3f;

        private readonly Dictionary<VehicleClass, float> _votes = [];
        private readonly List<(DateTimeOffset At, float X, float LinePosition)> _speedSamples = [];
        private float? _lastX;
        private DateTimeOffset _lastXAt;
        private int? _candidateLeader;
        private DateTimeOffset _coupledSince;
        private DateTimeOffset _lastCoupledAt;
        private float _minGap;
        private float _maxGap;

        public int TrackId => trackId;

        public bool Emitted { get; private set; }

        public int Frames { get; set; }

        public float? LastDistance { get; set; }

        public DateTimeOffset LastTimestamp { get; set; }

        public DateTimeOffset FirstTimestamp { get; set; }

        public float FirstDistance { get; set; }

        public DateTimeOffset? LastSignChangeAt { get; set; }

        public int? EstablishedSide { get; set; }

        public Crossing? Crossing { get; set; }

        /// <summary>Smoothed horizontal speed of the bottom-center point in pixels per second.</summary>
        public float VelocityX { get; private set; }

        /// <summary>Towing vehicle once the coupling is confirmed; the track is then a trailer.</summary>
        public int? LeaderId { get; private set; }

        public bool IsTrailer => LeaderId is not null;

        /// <summary>How far the track currently is beyond the line, measured towards the side it crossed to.</summary>
        public float DistanceBeyondLine => Crossing is { } c && LastDistance is float d ? d * c.Side : 0;

        /// <summary>False if the vehicle crossed but then backed up and left on its original side.</summary>
        public bool EndedOnFarSide => Crossing is { } c && EstablishedSide == c.Side;

        public void Vote(VehicleClass vehicleClass, float confidence) =>
            _votes[vehicleClass] = _votes.GetValueOrDefault(vehicleClass) + confidence;

        /// <summary>The class currently winning the vote.</summary>
        public VehicleClass LeadingClass => _votes.Count == 0 ? VehicleClass.Unknown : _votes.MaxBy(v => v.Value).Key;

        public void Release()
        {
            LeaderId = null;
            _candidateLeader = null;
        }

        public void UpdateVelocity(float x, DateTimeOffset timestamp)
        {
            if (_lastX is float lastX && timestamp > _lastXAt)
            {
                float velocity = (x - lastX) / (float)(timestamp - _lastXAt).TotalSeconds;
                VelocityX = Frames <= 2 ? velocity : VelocityX + (VelocitySmoothing * (velocity - VelocityX));
            }

            _lastX = x;
            _lastXAt = timestamp;
        }

        public void AddSpeedSample(DateTimeOffset timestamp, float x, float linePosition) =>
            _speedSamples.Add((timestamp, x, linePosition));

        public void Couple(int leaderId, float gap, float scale, DateTimeOffset timestamp, TrailerSettings trailers)
        {
            _minGap = MathF.Min(_minGap, gap);
            _maxGap = MathF.Max(_maxGap, gap);

            // A new leader, the gap changes (two independent vehicles), or the coupling was interrupted for longer
            // than a missed detection: start over from this frame, so separate phases never add up.
            if (_candidateLeader != leaderId
                || _maxGap - _minGap > trailers.MaxGapVariationRatio * scale
                || (timestamp - _lastCoupledAt).TotalSeconds > trailers.MaxInterruptionSeconds)
            {
                _candidateLeader = leaderId;
                _coupledSince = timestamp;
                _minGap = _maxGap = gap;
            }

            _lastCoupledAt = timestamp;
            if ((timestamp - _coupledSince).TotalSeconds >= trailers.MinCoupledSeconds)
            {
                LeaderId = leaderId;
            }
        }

        /// <summary>
        /// Checks a confirmed coupling. The trailer is released only after the gap has not fitted for as long as it
        /// took to confirm the coupling, so a few jittery boxes never make a real trailer count as a vehicle.
        /// </summary>
        public void Recheck(float? gap, float scale, DateTimeOffset timestamp, TrailerSettings trailers)
        {
            bool fits = gap is float g
                && MathF.Max(_maxGap, g) - MathF.Min(_minGap, g) <= trailers.MaxGapVariationRatio * scale;
            if (fits)
            {
                _lastCoupledAt = timestamp;
                return;
            }

            if ((timestamp - _lastCoupledAt).TotalSeconds > trailers.MinCoupledSeconds)
            {
                LeaderId = null;
                _candidateLeader = null;
            }
        }

        public VehiclePassageEvent Emit(int trailers, SpeedSettings? speed)
        {
            Emitted = true;
            Crossing crossing = Crossing!.Value;

            VehicleClass winner = VehicleClass.Unknown;
            float? share = null;
            float total = _votes.Values.Sum();
            if (total > 0)
            {
                KeyValuePair<VehicleClass, float> best = _votes.MaxBy(v => v.Value);
                winner = best.Key;
                share = best.Value / total;
            }

            float? kmh = null;
            if (speed is not null && _speedSamples.Count > 0)
            {
                DateTimeOffset first = _speedSamples[0].At;
                kmh = speed.Estimate([.. _speedSamples.Select(s => ((s.At - first).TotalSeconds, s.X, s.LinePosition))]);
            }

            var observation = new TrackObservation(
                _speedSamples.Count,
                _speedSamples.Count > 1 ? (_speedSamples[^1].At - _speedSamples[0].At).TotalSeconds : 0,
                (LastTimestamp - FirstTimestamp).TotalSeconds,
                FirstDistance,
                LastDistance ?? 0);
            return new VehiclePassageEvent(
                crossing.At, crossing.Direction, winner, share, kmh, trackId, Frames, crossing.LinePosition, trailers, observation);
        }
    }
}
