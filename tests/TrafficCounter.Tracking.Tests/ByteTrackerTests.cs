using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Tracking.Tests;

public class ByteTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 18, 0, 0, TimeSpan.FromHours(2));
    private const double Fps = 25;

    // A car seen from the side: 160 x 90 px, driving 500 px/s (≈ 20 px per frame).
    private static Detection Car(float centerX, float bottom, float confidence = 0.9f, float width = 160, float height = 90) =>
        new(new BoundingBox(centerX - (width / 2), bottom - height, width, height), VehicleClass.Car, confidence);

    private static DateTimeOffset Frame(int index) => T0 + TimeSpan.FromSeconds(index / Fps);

    /// <summary>Feeds frames and records, per frame, the id of the active track closest to each probe point.</summary>
    private sealed class Run
    {
        public ByteTracker Tracker { get; } = new();

        public List<TrackingResult> Results { get; } = [];

        public TrackingResult Step(int frame, params Detection[] detections)
        {
            TrackingResult result = Tracker.Update(detections, Frame(frame));
            Results.Add(result);
            return result;
        }

        public IEnumerable<int> AllActiveIds => Results.SelectMany(r => r.Active).Select(a => a.TrackId).Distinct();

        public IEnumerable<int> AllRemovedIds => Results.SelectMany(r => r.RemovedTrackIds);
    }

    [Fact]
    public void Single_vehicle_keeps_one_id()
    {
        var run = new Run();
        for (int i = 0; i < 60; i++)
        {
            run.Step(i, Car(100 + (20 * i), 700));
        }

        Assert.Single(run.AllActiveIds);
        Assert.Empty(run.Results[0].Active); // tentative in the first frame
        Assert.All(run.Results.Skip(1), r => Assert.Single(r.Active));
    }

    [Fact]
    public void Track_box_follows_the_vehicle()
    {
        var run = new Run();
        for (int i = 0; i < 30; i++)
        {
            run.Step(i, Car(100 + (20 * i), 700));
        }

        TrackedObject last = Assert.Single(run.Results[^1].Active);
        Assert.InRange(last.Box.BottomCenter.X, 100 + (20 * 29) - 5, 100 + (20 * 29) + 5);
        Assert.InRange(last.Box.Bottom, 695, 705);
    }

    [Fact]
    public void Single_frame_false_detection_never_becomes_a_track()
    {
        var run = new Run();
        run.Step(0, Car(500, 700));
        for (int i = 1; i < 40; i++)
        {
            run.Step(i);
        }

        Assert.Empty(run.AllActiveIds);
        Assert.Empty(run.AllRemovedIds);
    }

    [Fact]
    public void Vehicles_meeting_in_opposite_directions_keep_their_ids()
    {
        // Near lane (bottom 700) left-to-right, far lane (bottom 640) right-to-left: boxes overlap when they meet.
        var run = new Run();
        Dictionary<int, List<float>> xsById = [];
        for (int i = 0; i < 80; i++)
        {
            TrackingResult result = run.Step(i, Car(100 + (20 * i), 700), Car(1700 - (20 * i), 640));
            foreach (TrackedObject obj in result.Active)
            {
                xsById.TryAdd(obj.TrackId, []);
                xsById[obj.TrackId].Add(obj.Box.BottomCenter.X);
            }
        }

        Assert.Equal(2, xsById.Count);
        Assert.All(xsById.Values, xs =>
        {
            bool increasing = xs[^1] > xs[0];
            Assert.All(xs.Zip(xs.Skip(1)), p => Assert.True(increasing ? p.Second > p.First : p.Second < p.First, "a track changed direction – ids were swapped"));
        });
    }

    [Fact]
    public void Short_occlusion_keeps_the_id()
    {
        var run = new Run();
        for (int i = 0; i < 70; i++)
        {
            bool hidden = i is >= 30 and < 45; // 0.6 s behind a passing van
            run.Step(i, hidden ? [] : [Car(100 + (20 * i), 700)]);
        }

        Assert.Single(run.AllActiveIds);
        Assert.Empty(run.AllRemovedIds);
    }

    [Fact]
    public void Fast_vehicle_lost_for_long_is_removed_once_and_reappears_with_new_id()
    {
        var run = new Run();
        for (int i = 0; i < 20; i++)
        {
            run.Step(i, Car(100 + (20 * i), 700));
        }

        for (int i = 20; i < 60; i++) // 1.6 s without detections
        {
            run.Step(i);
        }

        Assert.Single(run.AllRemovedIds);

        for (int i = 60; i < 65; i++)
        {
            run.Step(i, Car(300, 700));
        }

        Assert.Equal(2, run.AllActiveIds.Count());
    }

    [Fact]
    public void Waiting_vehicle_hidden_for_three_seconds_keeps_the_id()
    {
        var run = new Run();
        for (int i = 0; i < 25; i++)
        {
            run.Step(i, Car(800, 640)); // stands still, waiting for oncoming traffic
        }

        for (int i = 25; i < 100; i++) // hidden for 3 s
        {
            run.Step(i);
        }

        for (int i = 100; i < 120; i++)
        {
            run.Step(i, Car(800 + (20 * (i - 100)), 640)); // drives off
        }

        Assert.Single(run.AllActiveIds);
        Assert.Empty(run.AllRemovedIds);
    }

    [Fact]
    public void Low_confidence_detections_continue_a_track()
    {
        var run = new Run();
        for (int i = 0; i < 60; i++)
        {
            float confidence = i is >= 20 and < 40 ? 0.25f : 0.9f; // e.g. rain or partial occlusion
            run.Step(i, Car(100 + (20 * i), 700, confidence));
        }

        Assert.Single(run.AllActiveIds);
        Assert.All(run.Results.Skip(1), r => Assert.Single(r.Active));
    }

    [Fact]
    public void Low_confidence_detections_alone_never_start_a_track()
    {
        var run = new Run();
        for (int i = 0; i < 30; i++)
        {
            run.Step(i, Car(100 + (20 * i), 700, confidence: 0.3f));
        }

        Assert.Empty(run.AllActiveIds);
    }

    [Fact]
    public void Vehicle_braking_while_hidden_is_stitched_to_its_track()
    {
        // Drives at 500 px/s, is hidden for 0.8 s while it brakes to a stop (it had to wait), then reappears.
        // The motion model predicts it 400 px further on, so IoU matching fails – stitching along the path must not.
        var run = new Run();
        float x = 100;
        for (int i = 0; i < 25; i++)
        {
            x = 100 + (20 * i);
            run.Step(i, Car(x, 700));
        }

        for (int i = 25; i < 45; i++)
        {
            run.Step(i);
        }

        float stoppedAt = x + 100; // travelled only 100 px while braking
        for (int i = 45; i < 70; i++)
        {
            run.Step(i, Car(stoppedAt + (i < 55 ? 0 : 10 * (i - 55)), 700));
        }

        Assert.Single(run.AllActiveIds);
        Assert.Equal(1, run.Tracker.StitchCount);
    }

    [Fact]
    public void Partially_visible_vehicle_is_stitched_despite_different_box_shape()
    {
        // After the occlusion only the upper half of the far-lane car is visible above a waiting car.
        var run = new Run();
        for (int i = 0; i < 25; i++)
        {
            run.Step(i, Car(1500 - (20 * i), 640));
        }

        for (int i = 25; i < 35; i++)
        {
            run.Step(i);
        }

        for (int i = 35; i < 45; i++)
        {
            run.Step(i, Car(1500 - (20 * i), 595, width: 160, height: 45));
        }

        Assert.Single(run.AllActiveIds);
    }

    [Fact]
    public void Vehicle_that_left_the_image_is_not_stitched_to_one_entering_there()
    {
        // A car on the far left (image starts at 0) marks the image extent; a car drives out at the right edge
        // (x = 1920) and a second one enters there right afterwards, driving the other way.
        var run = new Run();
        for (int i = 0; i < 25; i++)
        {
            run.Step(i, Car(1440 + (20 * i), 700), Car(80, 640, width: 160));
        }

        int leftId = run.Results[^1].Active.Single(a => a.Box.Center.X > 1000).TrackId;
        for (int i = 25; i < 40; i++)
        {
            run.Step(i, Car(80, 640, width: 160));
        }

        for (int i = 40; i < 60; i++)
        {
            run.Step(i, Car(1840 - (20 * (i - 40)), 700), Car(80, 640, width: 160));
        }

        Assert.DoesNotContain(run.Results.Skip(40).SelectMany(r => r.Active), a => a.TrackId == leftId);
        Assert.Equal(0, run.Tracker.StitchCount);
    }

    [Fact]
    public void Bicycle_is_not_stitched_to_a_car()
    {
        var run = new Run();
        for (int i = 0; i < 25; i++)
        {
            run.Step(i, new Detection(new BoundingBox(100 + (8 * i), 620, 80, 80), VehicleClass.Bicycle, 0.8f));
        }

        for (int i = 25; i < 35; i++)
        {
            run.Step(i);
        }

        for (int i = 35; i < 45; i++)
        {
            run.Step(i, Car(400 + (8 * (i - 35)), 700, width: 120, height: 80));
        }

        Assert.Equal(0, run.Tracker.StitchCount);
    }

    [Fact]
    public void Removed_ids_are_reported_exactly_once()
    {
        var run = new Run();
        for (int i = 0; i < 10; i++)
        {
            run.Step(i, Car(100 + (20 * i), 700), Car(1500 - (20 * i), 640));
        }

        for (int i = 10; i < 200; i++)
        {
            run.Step(i);
        }

        Assert.Equal(2, run.AllRemovedIds.Count());
        Assert.Equal(run.AllActiveIds.Order(), run.AllRemovedIds.Order());
    }

    [Fact]
    public void First_vehicle_of_a_period_hidden_in_the_middle_is_not_taken_for_leaving_the_image()
    {
        // Nothing has driven further right yet, so the extent of the detections ends at this car. Only the known
        // band edges tell that it stopped in the middle (hidden by a van) instead of leaving the image.
        var run = new Run();
        run.Tracker.SetHorizontalExtent(0, 1920);
        float x = 100;
        for (int i = 0; i < 25; i++)
        {
            x = 100 + (20 * i);
            run.Step(i, Car(x, 700));
        }

        for (int i = 25; i < 45; i++)
        {
            run.Step(i);
        }

        for (int i = 45; i < 70; i++)
        {
            run.Step(i, Car(x + (i < 55 ? 0 : 10 * (i - 55)), 700));
        }

        Assert.Single(run.AllActiveIds);
        Assert.Equal(1, run.Tracker.StitchCount);
    }

    [Fact]
    public void A_track_left_over_from_a_motion_gate_pause_does_not_take_over_another_vehicle()
    {
        // Car A drives right and is last seen at frame 20. The motion gate then skips the detector for 5 s (no
        // updates at all). Car B then appears further along A's path. Before, A's track was matched first and
        // removed only afterwards: its stitch path (velocity x gap x 2) reached B, and B continued A's identity.
        var run = new Run();
        for (int i = 0; i <= 20; i++)
        {
            run.Step(i, Car(100 + (20 * i), 700));
        }

        int carA = Assert.Single(run.Results[^1].Active).TrackId;
        for (int i = 0; i < 10; i++)
        {
            run.Step(20 + 125 + i, Car(900 + (20 * i), 700));
        }

        Assert.DoesNotContain(run.Results[^1].Active, t => t.TrackId == carA);
        Assert.Contains(carA, run.Results.SelectMany(r => r.RemovedTrackIds));
        Assert.Equal(0, run.Tracker.StitchCount);
    }

    [Fact]
    public void Dropped_frames_are_bridged_by_timestamps()
    {
        // Irregular frame times (a slow detector skips frames): positions follow real time.
        var tracker = new ByteTracker();
        int[] frames = [0, 1, 2, 5, 6, 11, 12, 13, 20, 21];
        HashSet<int> ids = [];
        foreach (int f in frames)
        {
            foreach (TrackedObject obj in tracker.Update([Car(100 + (20 * f), 700)], Frame(f)).Active)
            {
                ids.Add(obj.TrackId);
            }
        }

        Assert.Single(ids);
    }

    [Theory]
    [InlineData(Core.Operations.LightingCondition.Day, false, false)]
    [InlineData(Core.Operations.LightingCondition.Dusk, false, false)]
    [InlineData(Core.Operations.LightingCondition.Night, false, true)]
    [InlineData(Core.Operations.LightingCondition.Dusk, true, true)]
    [InlineData(Core.Operations.LightingCondition.Day, true, false)]
    public void Unsure_detections_start_tracks_only_at_night(Core.Operations.LightingCondition lighting, bool nightAtDusk, bool tracked)
    {
        // Headlights in the dark: the model stays around 0.3 although the vehicle is real.
        var tracker = new ByteTracker(new TrackerSettings { NightThresholdsAtDusk = nightAtDusk });
        TrackingResult result = null!;
        for (int i = 0; i < 5; i++)
        {
            result = tracker.Update([Car(100 + (20 * i), 600, confidence: 0.3f)], Frame(i), lighting);
        }

        Assert.Equal(tracked, result.Active.Count == 1);
    }
}
