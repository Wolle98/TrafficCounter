using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;
using TrafficCounter.Counting;

namespace TrafficCounter.Tracking.Tests;

/// <summary>
/// Tracker and counting engine together on the local traffic situations of ADR-0020, with a vertical counting
/// line from the far kerb (top) to the near kerb (bottom): moving left is direction A, moving right is B.
/// </summary>
public class TrackingAndCountingScenarioTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 18, 0, 0, TimeSpan.FromHours(2));
    private static readonly LineSegment Line = new(new Point2(960, 500), new Point2(960, 800));
    private const float NearLane = 740;
    private const float FarLane = 620;

    private static Detection Vehicle(float centerX, float bottom, float width = 160, float height = 90, float confidence = 0.9f) =>
        new(new BoundingBox(centerX - (width / 2), bottom - height, width, height), VehicleClass.Car, confidence);

    /// <summary>Runs the scene (detections per frame at 25 fps) and returns all passages, including a final flush.</summary>
    private static List<VehiclePassageEvent> Count(Func<int, IEnumerable<Detection>> scene, int frames)
    {
        var tracker = new ByteTracker();
        var counter = new CountingEngine(new CountingSettings(Line));
        List<VehiclePassageEvent> passages = [];
        for (int i = 0; i < frames; i++)
        {
            TrackingResult result = tracker.Update([.. scene(i)], T0 + TimeSpan.FromSeconds(i / 25.0));
            passages.AddRange(counter.Process(result));
        }

        passages.AddRange(counter.Flush());
        return passages;
    }

    [Fact]
    public void Two_vehicles_meeting_at_the_line_are_both_counted()
    {
        // Both reach the line at frame 43; the far-lane car is completely hidden by the near-lane van around it.
        List<VehiclePassageEvent> passages = Count(i =>
        {
            List<Detection> detections = [Vehicle(100 + (20 * i), NearLane, width: 220, height: 150)];
            float farX = 1820 - (20 * i);
            if (Math.Abs(farX - (100 + (20 * i))) > 180)
            {
                detections.Add(Vehicle(farX, FarLane));
            }

            return detections;
        }, frames: 90);

        Assert.Equal(2, passages.Count);
        Assert.Single(passages, p => p.Direction == TravelDirection.B);
        Assert.Single(passages, p => p.Direction == TravelDirection.A);
    }

    [Fact]
    public void Vehicle_waiting_on_the_line_while_oncoming_traffic_hides_it_is_counted_once()
    {
        // Far-lane car (moving left) stops across the line for 4 s; a near-lane van passes in front of it.
        List<VehiclePassageEvent> passages = Count(i =>
        {
            List<Detection> detections = [];
            float waitingX = i < 30 ? 1500 - (20 * i) : i < 130 ? 900 : 900 - (20 * (i - 130));
            float vanX = -200 + (20 * (i - 40));
            bool hidden = i >= 40 && Math.Abs(vanX - waitingX) < 200;
            if (!hidden)
            {
                detections.Add(Vehicle(waitingX, FarLane));
            }

            if (i >= 40)
            {
                detections.Add(Vehicle(vanX, NearLane, width: 240, height: 160));
            }

            return detections;
        }, frames: 180);

        Assert.Equal(1, passages.Count(p => p.Direction == TravelDirection.A));
        Assert.Equal(1, passages.Count(p => p.Direction == TravelDirection.B));
    }

    [Fact]
    public void Vehicle_reversing_back_before_crossing_completely_is_not_counted()
    {
        // Enters on the wrong lane, gets about 2 m (220 px) past the line, meets oncoming traffic,
        // backs up and leaves where it came from.
        List<VehiclePassageEvent> passages = Count(i =>
        {
            float x = i < 40 ? 400 + (20 * i) : 1200 - (20 * (i - 40));
            return x < -100 ? [] : [Vehicle(x, FarLane)];
        }, frames: 120);

        Assert.Empty(passages);
    }

    /// <summary>A box of the given size whose left edge is at <paramref name="left"/>.</summary>
    private static Detection Box(float left, float bottom, float width, float height, VehicleClass vehicleClass = VehicleClass.Car) =>
        new(new BoundingBox(left, bottom - height, width, height), vehicleClass, 0.85f);

    [Fact]
    public void Car_with_trailer_is_counted_once_with_the_trailer()
    {
        // Moving left on the far lane: the trailer (detected as truck) follows 30 px behind the car.
        List<VehiclePassageEvent> passages = Count(i =>
        {
            float carLeft = 1900 - (20 * i);
            return [Box(carLeft, FarLane, 160, 90), Box(carLeft + 160 + 30, FarLane + 4, 140, 100, VehicleClass.Truck)];
        }, frames: 120);

        VehiclePassageEvent passage = Assert.Single(passages);
        Assert.Equal(TravelDirection.A, passage.Direction);
        Assert.Equal(VehicleClass.Car, passage.Class);
        Assert.Equal(1, passage.TrailerCount);
    }

    [Fact]
    public void Tractor_with_two_trailers_is_counted_once()
    {
        // Moving right slowly (about 20 km/h) on the near lane.
        List<VehiclePassageEvent> passages = Count(i =>
        {
            float tractorLeft = 520 + (10 * i);
            return
            [
                Box(tractorLeft, NearLane, 120, 110, VehicleClass.Truck),
                Box(tractorLeft - 25 - 150, NearLane, 150, 95, VehicleClass.Truck),
                Box(tractorLeft - 25 - 150 - 25 - 150, NearLane, 150, 95, VehicleClass.Truck),
            ];
        }, frames: 150);

        VehiclePassageEvent passage = Assert.Single(passages);
        Assert.Equal(TravelDirection.B, passage.Direction);
        Assert.Equal(2, passage.TrailerCount);
    }

    [Fact]
    public void Two_cars_following_closely_with_changing_gap_are_both_counted()
    {
        // The second car starts right behind the first but is 3 px per frame slower (about 4 km/h).
        List<VehiclePassageEvent> passages = Count(i =>
        [
            Box(1900 - (23 * i), FarLane, 160, 90),
            Box(1900 + 160 + 10 - (20 * i), FarLane, 160, 90),
        ], frames: 120);

        Assert.Equal(2, passages.Count);
        Assert.All(passages, p => Assert.Equal(0, p.TrailerCount));
    }

    [Fact]
    public void Two_short_coupling_phases_with_a_break_do_not_add_up()
    {
        // Car 2 follows car 1 at the same speed and a constant gap for 0.4 s, car 1 is then missed by the detector
        // for 0.32 s, then both are seen alike for another 0.44 s before car 2 falls back. Neither phase lasts the
        // 0.6 s a coupling needs; together they would, and car 2 would wrongly become a trailer, i.e. not counted.
        List<VehiclePassageEvent> passages = Count(i =>
        {
            float leaderLeft = 1900 - (20 * i);
            float gap = i < 29 ? 30 : 30 + (4 * (i - 29));
            List<Detection> detections = [Box(leaderLeft + 160 + gap, FarLane, 160, 90)];
            if (i is < 10 or >= 18)
            {
                detections.Add(Box(leaderLeft, FarLane, 160, 90));
            }

            return detections;
        }, frames: 140);

        Assert.Equal(2, passages.Count);
        Assert.All(passages, p => Assert.Equal(0, p.TrailerCount));
    }

    [Fact]
    public void Car_coupled_by_mistake_is_released_when_it_falls_back()
    {
        // Car 2 drives exactly like a trailer for 1 s, then falls back (gap grows 4 px per frame) long before car 1
        // is counted. It must be released again and counted on its own.
        List<VehiclePassageEvent> passages = Count(i =>
        {
            float leaderLeft = 1900 - (20 * i);
            float gap = i < 25 ? 30 : 30 + (4 * (i - 25));
            return [Box(leaderLeft, FarLane, 160, 90), Box(leaderLeft + 160 + gap, FarLane, 160, 90)];
        }, frames: 160);

        Assert.Equal(2, passages.Count);
        Assert.All(passages, p => Assert.Equal(0, p.TrailerCount));
    }

    [Fact]
    public void Slow_queue_bumper_to_bumper_is_counted_as_separate_vehicles()
    {
        // Creeping at walking pace with a constant gap of 20 px: too slow to be taken for a coupling.
        List<VehiclePassageEvent> passages = Count(i =>
        [
            Box(1300 - (3 * i), FarLane, 160, 90),
            Box(1300 + 160 + 20 - (3 * i), FarLane, 160, 90),
        ], frames: 400);

        Assert.Equal(2, passages.Count);
    }

    [Fact]
    public void Trailer_entering_the_picture_late_is_still_attached_to_its_towing_vehicle()
    {
        // Close camera: the car is long in the image, its trailer appears only when the car is near the line and
        // is recognised after the car would have been emitted without the emit delay.
        List<VehiclePassageEvent> passages = Count(i =>
        {
            float carLeft = 1300 - (40 * i);
            float trailerLeft = carLeft + 700 + 40;
            List<Detection> detections = [Box(carLeft, FarLane, 700, 200)];
            if (trailerLeft + 400 <= 1920)
            {
                detections.Add(Box(trailerLeft, FarLane, 400, 220, VehicleClass.Truck));
            }

            return detections;
        }, frames: 150);

        VehiclePassageEvent passage = Assert.Single(passages);
        Assert.Equal(1, passage.TrailerCount);
    }
}
