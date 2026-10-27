using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.Tracking;

/// <summary>
/// ByteTrack (Zhang et al. 2022) with two additions from ADR-0020: a lost-track timeout that depends on speed,
/// and stitching of lost tracks to detections along their path, so a vehicle hidden by oncoming traffic keeps
/// its identity even if it slowed down meanwhile. Not thread-safe.
/// </summary>
public sealed class ByteTracker(TrackerSettings? settings = null) : ITracker
{
    private readonly TrackerSettings _settings = settings ?? new TrackerSettings();
    private readonly List<Track> _tracks = [];
    private int _nextId = 1;
    private DateTimeOffset _lastUpdate;

    // Edges of the analysed area for the exit check: set by the pipeline, or else the extent of all detections so far
    // (which only reaches the real edges once vehicles have driven through the whole width).
    private float _minLeft = float.MaxValue;
    private float _maxRight = float.MinValue;
    private bool _extentKnown;

    public void SetHorizontalExtent(float left, float right)
    {
        (_minLeft, _maxRight, _extentKnown) = (left, right, true);
    }

    /// <summary>Number of lost tracks recovered by stitching since creation (for diagnostics).</summary>
    public int StitchCount { get; private set; }

    public TrackingResult Update(
        IReadOnlyList<Detection> detections, DateTimeOffset timestamp, LightingCondition lighting = LightingCondition.Unknown)
    {
        bool night = lighting == LightingCondition.Night || (lighting == LightingCondition.Dusk && _settings.NightThresholdsAtDusk);
        float highConfidence = night ? _settings.NightHighConfidence : _settings.HighConfidence;
        float newTrackConfidence = night ? _settings.NightNewTrackConfidence : _settings.NewTrackConfidence;
        _lastUpdate = timestamp;
        foreach (Detection detection in _extentKnown ? [] : detections)
        {
            _minLeft = Math.Min(_minLeft, detection.Box.Left);
            _maxRight = Math.Max(_maxRight, detection.Box.Right);
        }

        // 0. Tracks not seen for longer than their timeout are gone before anything is matched. Otherwise a track
        //    left over from before a pause of the motion gate (no updates for up to several seconds) would be
        //    stitched to a different vehicle further along its path: the stitch path grows with the gap.
        List<int> removed = [];
        RemoveExpired(timestamp, _tracks, removed);

        foreach (Track track in _tracks)
        {
            track.Predict(timestamp);
        }

        // Two-wheelers may have their own, lower thresholds; the lower of those and the current ones applies.
        float HighFor(Detection d) =>
            IsTwoWheeler(d.Class) && _settings.TwoWheelerHighConfidence is float t ? Math.Min(t, highConfidence) : highConfidence;
        float NewTrackFor(Detection d) =>
            IsTwoWheeler(d.Class) && _settings.TwoWheelerNewTrackConfidence is float t ? Math.Min(t, newTrackConfidence) : newTrackConfidence;

        List<Detection> high = [.. detections.Where(d => d.Confidence >= HighFor(d))];
        List<Detection> low = [.. detections.Where(d => d.Confidence >= _settings.LowConfidence && d.Confidence < HighFor(d))];

        // 1. High detections vs. confirmed tracks (tracked and lost), by IoU with the predicted box.
        List<Track> confirmed = [.. _tracks.Where(t => t.IsConfirmed)];
        (List<Detection> highLeft, List<Track> confirmedLeft) =
            Associate(high, confirmed, _settings.FirstMatchMaxCost, (t, d) => t.Update(d, timestamp));

        // 2. Low detections continue tracks that are still being tracked (ByteTrack's key idea).
        List<Track> trackedLeft = [.. confirmedLeft.Where(t => t.State == TrackState.Tracked)];
        (List<Detection> lowLeft, List<Track> unmatchedTracked) =
            Associate(low, trackedLeft, _settings.SecondMatchMaxCost, (t, d) => t.Update(d, timestamp));

        foreach (Track track in unmatchedTracked)
        {
            track.MarkLost();
        }

        // 3. Stitch lost tracks to remaining detections along their path (occlusion, braking while hidden).
        //    Before the tentative tracks: a vehicle reappearing behind an occluder often starts a tentative track
        //    in the first frame after the gap, which would otherwise take the detection from the lost track.
        List<Track> lost = [.. _tracks.Where(t => t.IsConfirmed && t.State == TrackState.Lost)];
        List<Detection> leftovers = [.. highLeft, .. lowLeft];
        (leftovers, _) = Associate(leftovers, lost, _settings.StitchMaxDistance, (t, d) =>
        {
            t.Stitch(d, timestamp);
            StitchCount++;
        }, StitchCost);
        highLeft = [.. leftovers.Where(d => d.Confidence >= HighFor(d))];

        // 4. Tentative tracks need a second detection to become confirmed.
        List<Track> tentative = [.. _tracks.Where(t => !t.IsConfirmed)];
        (highLeft, List<Track> tentativeLeft) =
            Associate(highLeft, tentative, _settings.TentativeMatchMaxCost, (t, d) => t.Update(d, timestamp));

        foreach (Track track in tentativeLeft)
        {
            track.MarkRemoved();
        }

        // 5. New tentative tracks from confident, unexplained detections.
        foreach (Detection detection in highLeft.Where(d => d.Confidence >= NewTrackFor(d)))
        {
            _tracks.Add(new Track(_nextId++, detection, timestamp));
        }

        // 6. Remove tracks lost for too long; the timeout depends on how fast they were moving.
        RemoveExpired(timestamp, [.. _tracks.Where(t => t.State == TrackState.Lost)], removed);

        List<TrackedObject> active =
        [
            .. _tracks
                .Where(t => t.IsConfirmed && t.State == TrackState.Tracked && t.LastSeen == timestamp)
                .Select(t => new TrackedObject(t.Id, t.Box, t.Class, t.Confidence)),
        ];
        return new TrackingResult(timestamp, active, removed);
    }

    private double StitchCost(Track track, Detection detection)
    {
        // A vehicle that drove out of the image does not come back; a vehicle entering there is a different one.
        // Two-wheelers and four-wheelers are never confused by occlusion, only by the detector on single frames.
        if (HasLeftImage(track) || IsTwoWheeler(track.Class) != IsTwoWheeler(detection.Class))
        {
            return double.PositiveInfinity;
        }

        BoundingBox predicted = track.Box;
        (float typicalWidth, float typicalHeight) = track.TypicalSize;
        double ratio = detection.Box.Height / Math.Max(1f, typicalHeight);
        if (ratio > _settings.StitchMaxSizeRatio || ratio < 1 / _settings.StitchMaxSizeRatio)
        {
            return double.PositiveInfinity;
        }

        double distance = DistanceToSegment(detection.Box.Center, track.LastObservedCenter, predicted.Center);
        if (track.AverageVelocity is Point2 velocity)
        {
            float gap = (float)(_lastUpdate - track.LastSeen).TotalSeconds;
            Point2 ahead = track.LastObservedCenter + (velocity * (gap * (float)_settings.StitchForwardFactor));
            distance = Math.Min(distance, DistanceToSegment(detection.Box.Center, track.LastObservedCenter, ahead));
        }

        return distance / Math.Max(typicalWidth, typicalHeight);
    }

    /// <summary>Last seen touching the left or right image edge while moving towards it.</summary>
    private bool HasLeftImage(Track track)
    {
        if (track.AverageVelocity is not Point2 velocity || _maxRight <= _minLeft)
        {
            return false;
        }

        BoundingBox box = track.LastObservedBox;
        float margin = (float)_settings.StitchEdgeMargin * box.Width;
        return (velocity.X > 0 && box.Right >= _maxRight - margin) || (velocity.X < 0 && box.Left <= _minLeft + margin);
    }

    /// <summary>Removes the given tracks whose last detection is older than their timeout (it depends on their speed).</summary>
    private void RemoveExpired(DateTimeOffset timestamp, List<Track> candidates, List<int> removed)
    {
        foreach (Track track in candidates.Where(t => t.State != TrackState.Removed))
        {
            TimeSpan timeout = track.SpeedInHeightsPerSecond < _settings.SlowSpeed
                ? _settings.LostTimeoutStationary
                : _settings.LostTimeoutMoving;
            if (timestamp - track.LastSeen > timeout)
            {
                track.MarkRemoved();
                removed.Add(track.Id);
            }
        }

        _tracks.RemoveAll(t => t.State == TrackState.Removed);
    }

    private static bool IsTwoWheeler(VehicleClass vehicleClass) => vehicleClass is VehicleClass.Bicycle or VehicleClass.Motorcycle;

    private static (List<Detection> Unmatched, List<Track> UnmatchedTracks) Associate(
        List<Detection> detections,
        List<Track> tracks,
        double maxCost,
        Action<Track, Detection> onMatch,
        Func<Track, Detection, double>? costFunction = null)
    {
        costFunction ??= static (t, d) => 1 - t.Box.IntersectionOverUnion(d.Box);
        var cost = new double[tracks.Count, detections.Count];
        for (int t = 0; t < tracks.Count; t++)
        {
            for (int d = 0; d < detections.Count; d++)
            {
                cost[t, d] = costFunction(tracks[t], detections[d]);
            }
        }

        AssignmentResult result = LinearAssignment.Solve(cost, maxCost);
        foreach ((int t, int d) in result.Matches)
        {
            onMatch(tracks[t], detections[d]);
        }

        return ([.. result.UnmatchedColumns.Select(i => detections[i])], [.. result.UnmatchedRows.Select(i => tracks[i])]);
    }

    private static double DistanceToSegment(Point2 point, Point2 start, Point2 end)
    {
        Point2 segment = end - start;
        float lengthSquared = segment.Dot(segment);
        float t = lengthSquared == 0 ? 0 : Math.Clamp((point - start).Dot(segment) / lengthSquared, 0, 1);
        return (point - (start + (segment * t))).Length;
    }
}
