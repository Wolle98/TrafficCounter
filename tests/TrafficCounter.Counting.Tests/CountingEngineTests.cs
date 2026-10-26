using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Counting.Tests;

public class CountingEngineTests
{
    // Vertical counting line at x = 500 from y = 100 to y = 400 (road runs horizontally).
    // Looking from Start (top) to End (bottom), the right-hand side in image coordinates is x < 500,
    // so a vehicle driving right-to-left crosses onto the positive side: direction A.
    private static readonly LineSegment Line = new(new Point2(500, 100), new Point2(500, 400));

    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 12, 0, 0, TimeSpan.FromHours(2));
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(40);

    // Default: emit only when the track ends, so tests see the full track.
    private static CountingEngine CreateEngine(float emitDistance = 10_000, float emitDelay = 0) =>
        new(new CountingSettings(Line, HysteresisPixels: 8, EmitDistancePixels: emitDistance, EmitDelaySeconds: emitDelay));

    /// <summary>Box whose bottom-center lies at (x, bottomY).</summary>
    private static TrackedObject Obj(int id, float x, float bottomY = 300, VehicleClass cls = VehicleClass.Car, float conf = 0.9f) =>
        new(id, new BoundingBox(x - 40, bottomY - 50, 80, 50), cls, conf);

    /// <summary>Drives one track along the given x positions, one frame each, then ends it.</summary>
    private static List<VehiclePassageEvent> Drive(CountingEngine engine, int id, IEnumerable<float> xs, float bottomY = 300)
    {
        List<VehiclePassageEvent> events = [];
        int frame = 0;
        foreach (float x in xs)
        {
            events.AddRange(engine.Process(new TrackingResult(T0 + (FrameInterval * frame++), [Obj(id, x, bottomY)], [])));
        }

        events.AddRange(engine.Process(new TrackingResult(T0 + (FrameInterval * frame), [], [id])));
        return events;
    }

    private static IEnumerable<float> Range(float from, float to, float step)
    {
        for (float x = from; step > 0 ? x <= to : x >= to; x += step)
        {
            yield return x;
        }
    }

    [Fact]
    public void Passages_are_pending_from_near_the_line_until_emitted()
    {
        var engine = CreateEngine(emitDistance: 100);
        Assert.False(engine.HasPendingPassages);

        engine.Process(new TrackingResult(T0, [Obj(1, 900)], []));
        Assert.False(engine.HasPendingPassages); // far from the line

        int frame = 1;
        foreach (float x in Range(880, 560, -20))
        {
            engine.Process(new TrackingResult(T0 + (FrameInterval * frame++), [Obj(1, x)], []));
        }

        Assert.True(engine.HasPendingPassages); // standing just before the line

        List<VehiclePassageEvent> events = [];
        foreach (float x in Range(540, 300, -20))
        {
            events.AddRange(engine.Process(new TrackingResult(T0 + (FrameInterval * frame++), [Obj(1, x)], [])));
        }

        Assert.Single(events);
        Assert.False(engine.HasPendingPassages); // emitted
    }

    [Fact]
    public void Track_crossing_right_to_left_is_counted_once_as_direction_A()
    {
        List<VehiclePassageEvent> events = Drive(CreateEngine(), 1, Range(700, 300, -20));

        VehiclePassageEvent passage = Assert.Single(events);
        Assert.Equal(TravelDirection.A, passage.Direction);
        Assert.Equal(1, passage.TrackId);
        Assert.Equal(VehicleClass.Car, passage.Class);
    }

    [Fact]
    public void Track_crossing_left_to_right_is_direction_B()
    {
        VehiclePassageEvent passage = Assert.Single(Drive(CreateEngine(), 1, Range(300, 700, 20)));
        Assert.Equal(TravelDirection.B, passage.Direction);
    }

    [Fact]
    public void Crossing_time_is_interpolated_between_frames()
    {
        // x: 530 (frame 0), 510 (1), 490 (2), 470 (3) -> sign change halfway between frames 1 and 2.
        VehiclePassageEvent passage = Assert.Single(Drive(CreateEngine(), 1, [530, 510, 490, 470]));
        Assert.Equal(T0 + (FrameInterval * 1.5), passage.PassedAt);
    }

    [Fact]
    public void Track_that_does_not_cross_is_not_counted() =>
        Assert.Empty(Drive(CreateEngine(), 1, Range(900, 600, -20)));

    [Fact]
    public void Jitter_within_hysteresis_band_is_not_counted() =>
        Assert.Empty(Drive(CreateEngine(), 1, [520, 505, 495, 504, 497, 506, 494, 520]));

    [Fact]
    public void Track_first_seen_on_the_line_is_not_counted() =>
        Assert.Empty(Drive(CreateEngine(), 1, [502, 498, 490, 470, 450]));

    [Fact]
    public void Driving_back_and_forth_counts_only_the_first_crossing()
    {
        VehiclePassageEvent passage = Assert.Single(Drive(CreateEngine(), 1, [560, 540, 520, 480, 460, 480, 520, 540, 480, 460]));
        Assert.Equal(TravelDirection.A, passage.Direction);
    }

    [Fact]
    public void Crossing_beyond_segment_end_is_not_counted() =>
        // Line spans y 100..400 (+10 % margin = 70..430); this track passes at y = 600.
        Assert.Empty(Drive(CreateEngine(), 1, Range(700, 300, -20), bottomY: 600));

    [Fact]
    public void Too_short_track_is_not_counted() =>
        Assert.Empty(Drive(CreateEngine(), 1, [520, 480]));

    [Fact]
    public void Passage_is_emitted_once_track_is_far_enough_beyond_the_line()
    {
        CountingEngine engine = CreateEngine(emitDistance: 50);
        float[] xs = [560, 540, 520, 480, 460, 440, 420, 400];
        List<int> emittedAtFrame = [];
        for (int i = 0; i < xs.Length; i++)
        {
            if (engine.Process(new TrackingResult(T0 + (FrameInterval * i), [Obj(1, xs[i])], [])).Count > 0)
            {
                emittedAtFrame.Add(i);
            }
        }

        // x = 440 is the first position at least 50 px beyond the line (x = 500).
        Assert.Equal([5], emittedAtFrame);

        // Ending the track later must not emit it a second time.
        Assert.Empty(engine.Process(new TrackingResult(T0 + TimeSpan.FromSeconds(1), [], [1])));
    }

    [Fact]
    public void Passage_of_a_visible_track_waits_for_the_emit_delay()
    {
        // Crosses at frame 2.5 (100 ms); far enough beyond the line from frame 5, but emitted only 1 s after crossing.
        CountingEngine engine = CreateEngine(emitDistance: 50, emitDelay: 1);
        List<int> emittedAtFrame = [];
        for (int i = 0; i < 40; i++)
        {
            float x = Math.Max(550 - (20 * i), 300);
            if (engine.Process(new TrackingResult(T0 + (FrameInterval * i), [Obj(1, x)], [])).Count > 0)
            {
                emittedAtFrame.Add(i);
            }
        }

        Assert.Equal([28], emittedAtFrame);
    }

    [Fact]
    public void Vehicle_waiting_on_the_line_is_emitted_only_after_moving_on()
    {
        CountingEngine engine = CreateEngine(emitDistance: 50);
        List<float> xs = [560, 540, 520, 480];
        xs.AddRange(Enumerable.Repeat(485f, 100)); // waits ~4 s just beyond the line for oncoming traffic
        xs.AddRange([470, 440, 400]);

        List<(int Frame, VehiclePassageEvent Passage)> events = [];
        for (int i = 0; i < xs.Count; i++)
        {
            foreach (VehiclePassageEvent e in engine.Process(new TrackingResult(T0 + (FrameInterval * i), [Obj(1, xs[i])], [])))
            {
                events.Add((i, e));
            }
        }

        (int frame, VehiclePassageEvent passage) = Assert.Single(events);
        Assert.Equal(xs.Count - 2, frame);
        Assert.Equal(T0 + (FrameInterval * 2.5), passage.PassedAt); // time of crossing, not of emitting
    }

    [Fact]
    public void Vehicle_backing_up_to_its_original_side_is_not_counted() =>
        // Crosses, then reverses to let oncoming traffic pass and leaves the view where it came from.
        Assert.Empty(Drive(CreateEngine(), 1, [560, 540, 520, 480, 470, 490, 520, 560, 600, 650]));

    [Fact]
    public void Position_along_the_line_is_reported()
    {
        // Line spans y 100..400; bottom at y = 300 is two thirds along it.
        VehiclePassageEvent passage = Assert.Single(Drive(CreateEngine(), 1, Range(700, 300, -20), bottomY: 300));
        Assert.Equal(2f / 3f, passage.LinePosition, 3);
    }

    [Fact]
    public void Vehicle_changing_lanes_while_crossing_is_counted_once()
    {
        // Enters on the "wrong" lane (y = 200) to pass parked cars and returns to its lane (y = 350) at the line.
        CountingEngine engine = CreateEngine();
        List<VehiclePassageEvent> events = [];
        for (int i = 0; i < 20; i++)
        {
            float y = 200 + (MathF.Min(i, 15) * 10);
            events.AddRange(engine.Process(new TrackingResult(T0 + (FrameInterval * i), [Obj(1, 700 - (i * 20), bottomY: y)], [])));
        }

        events.AddRange(engine.Process(new TrackingResult(T0 + TimeSpan.FromSeconds(1), [], [1])));

        Assert.Equal(TravelDirection.A, Assert.Single(events).Direction);
    }

    [Fact]
    public void Class_is_decided_by_confidence_weighted_vote()
    {
        CountingEngine engine = CreateEngine();
        (float X, VehicleClass Class, float Conf)[] frames =
        [
            (560, VehicleClass.Truck, 0.4f),
            (540, VehicleClass.Van, 0.8f),
            (520, VehicleClass.Van, 0.7f),
            (480, VehicleClass.Truck, 0.5f),
            (460, VehicleClass.Van, 0.6f),
        ];
        for (int i = 0; i < frames.Length; i++)
        {
            engine.Process(new TrackingResult(T0 + (FrameInterval * i), [Obj(7, frames[i].X, cls: frames[i].Class, conf: frames[i].Conf)], []));
        }

        VehiclePassageEvent passage = Assert.Single(engine.Process(new TrackingResult(T0 + TimeSpan.FromSeconds(1), [], [7])));
        Assert.Equal(VehicleClass.Van, passage.Class);
        Assert.Equal(2.1f / 3.0f, passage.ClassConfidence!.Value, 4);
    }

    [Fact]
    public void Two_simultaneous_tracks_in_opposite_directions_are_counted_separately()
    {
        CountingEngine engine = CreateEngine();
        List<VehiclePassageEvent> events = [];
        for (int i = 0; i < 20; i++)
        {
            events.AddRange(engine.Process(new TrackingResult(
                T0 + (FrameInterval * i),
                [Obj(1, 700 - (i * 20), bottomY: 200), Obj(2, 300 + (i * 20), bottomY: 350)],
                [])));
        }

        events.AddRange(engine.Process(new TrackingResult(T0 + TimeSpan.FromSeconds(1), [], [1, 2])));

        Assert.Equal(2, events.Count);
        Assert.Equal(TravelDirection.A, events.Single(e => e.TrackId == 1).Direction);
        Assert.Equal(TravelDirection.B, events.Single(e => e.TrackId == 2).Direction);
    }

    /// <summary>Several tracks moving together; each frame gives every track's x position.</summary>
    private static List<VehiclePassageEvent> DriveAll(CountingEngine engine, int frames, Func<int, IEnumerable<TrackedObject>> scene)
    {
        List<VehiclePassageEvent> events = [];
        HashSet<int> ids = [];
        for (int i = 0; i < frames; i++)
        {
            List<TrackedObject> active = [.. scene(i)];
            ids.UnionWith(active.Select(o => o.TrackId));
            events.AddRange(engine.Process(new TrackingResult(T0 + (FrameInterval * i), active, [])));
        }

        events.AddRange(engine.Process(new TrackingResult(T0 + (FrameInterval * frames), [], [.. ids])));
        return events;
    }

    [Fact]
    public void Cyclists_riding_close_behind_each_other_are_both_counted()
    {
        // Moving left with a constant 20 px gap for far longer than a coupling needs (issue #2).
        List<VehiclePassageEvent> events = DriveAll(CreateEngine(), 50, i =>
            [Obj(1, 1100 - (20 * i), cls: VehicleClass.Bicycle), Obj(2, 1200 - (20 * i), cls: VehicleClass.Bicycle)]);

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(0, e.TrailerCount));
    }

    [Fact]
    public void Second_track_of_one_long_vehicle_in_the_same_lane_is_not_counted_again()
    {
        // A bus split into a front and a rear track that cross the line 0.2 s apart (issue #3).
        List<VehiclePassageEvent> events = DriveAll(CreateEngine(), 30, i =>
            [Obj(1, 900 - (40 * i), cls: VehicleClass.Bus), Obj(2, 1100 - (40 * i), cls: VehicleClass.Bus)]);

        VehiclePassageEvent passage = Assert.Single(events);
        Assert.Equal(VehicleClass.Bus, passage.Class);
    }

    [Fact]
    public void Vehicles_crossing_at_the_same_time_in_other_lanes_or_on_two_wheels_are_all_counted()
    {
        List<VehiclePassageEvent> cars = DriveAll(CreateEngine(), 30, i =>
            [Obj(1, 900 - (40 * i), bottomY: 200), Obj(2, 920 - (40 * i), bottomY: 350)]);
        List<VehiclePassageEvent> cyclistsSideBySide = DriveAll(CreateEngine(), 30, i =>
            [Obj(1, 900 - (20 * i), bottomY: 300, cls: VehicleClass.Bicycle), Obj(2, 902 - (20 * i), bottomY: 310, cls: VehicleClass.Bicycle)]);

        Assert.Equal(2, cars.Count);
        Assert.Equal(2, cyclistsSideBySide.Count);
    }

    [Fact]
    public void A_detected_trailer_is_counted_with_its_towing_vehicle_and_never_on_its_own()
    {
        // Car moving left, a box of class Trailer 20 px behind it; then a trailer alone (ADR-0047).
        List<VehiclePassageEvent> combination = DriveAll(CreateEngine(), 50, i =>
            [Obj(1, 1100 - (20 * i)), Obj(2, 1200 - (20 * i), cls: VehicleClass.Trailer)]);
        List<VehiclePassageEvent> trailerAlone = DriveAll(CreateEngine(), 50, i =>
            [Obj(1, 1100 - (20 * i), cls: VehicleClass.Trailer)]);

        VehiclePassageEvent passage = Assert.Single(combination);
        Assert.Equal(VehicleClass.Car, passage.Class);
        Assert.Equal(1, passage.TrailerCount);
        Assert.Empty(trailerAlone);
    }

    [Fact]
    public void Flush_emits_pending_passages_and_clears_state()
    {
        CountingEngine engine = CreateEngine();
        float[] xs = [560, 540, 520, 480, 460];
        for (int i = 0; i < xs.Length; i++)
        {
            engine.Process(new TrackingResult(T0 + (FrameInterval * i), [Obj(1, xs[i])], []));
        }

        Assert.Single(engine.Flush());
        Assert.Equal(0, engine.ActiveTrackCount);
    }
}
