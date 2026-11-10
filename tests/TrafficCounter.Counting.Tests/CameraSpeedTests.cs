using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Counting.Tests;

/// <summary>Speeds with a camera model (ADR-0056), on a synthetic camera beside a straight road.</summary>
public class CameraSpeedTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 31, 12, 0, 0, TimeSpan.FromHours(1));

    // Camera 5 m above the road at the origin, looking north and 25° down, with barrel distortion. The road runs
    // east-west; its far kerb is 16 m north of the camera, the near kerb 10 m.
    private static readonly double Tilt = 25 * Math.PI / 180;

    private static readonly CameraModel Camera = new CameraModel(
        1920, 1080, Focal: 1000, PrincipalX: 960, PrincipalY: 540, K1: -0.2, DistortionScale: 960,
        Rotation: [(Math.PI / 2) + Tilt, 0, 0], Center: [0, 0, 5], Ground: [0, 0, 0],
        RoadOrigin: [0, 16], RoadAxis: [1, 0], AcrossAxis: [0, -1]).ForFrame(1920, 1080);

    [Theory]
    [InlineData(0, 2)]
    [InlineData(6, 0.5)]
    [InlineData(-7, 5)]
    public void Road_positions_survive_projection_and_back(double along, double across)
    {
        (double x, double y, double z) = Camera.OnRoad(along, across);
        (double px, double py) = Camera.Project(x, y, z)!.Value;

        (double backAlong, double backAcross) = Camera.ToRoad(px, py);

        Assert.Equal(along, backAlong, 3);
        Assert.Equal(across, backAcross, 3);
    }

    [Theory]
    [InlineData(5, 1.5, true)]
    [InlineData(-6, 3, false)]
    public void A_vertical_edge_is_found_from_its_outermost_column(double along, double height, bool right)
    {
        const double Lane = 4;
        (double x, double y, double z) = Camera.OnRoad(along, Lane);
        double top = Camera.Project(x, y, z + height)!.Value.Y;
        double column = Enumerable.Range(0, 51)
            .Select(i => Camera.Project(x, y, z + (height * i / 50))!.Value.X)
            .Aggregate(right ? double.MinValue : double.MaxValue, (a, b) => right ? Math.Max(a, b) : Math.Min(a, b));

        double? found = Camera.AlongOfEdge(column, Lane, top, right);

        Assert.NotNull(found);
        Assert.Equal(along, found!.Value, 1);
    }

    [Theory]
    [InlineData(30, 4.5, 1.8, 1.5, 4.0, VehicleClass.Car)]
    [InlineData(50, 4.5, 1.8, 1.5, 1.5, VehicleClass.Car)]
    [InlineData(25, 12, 2.55, 3.1, 4.5, VehicleClass.Bus)]
    public void Cars_and_buses_get_their_speed(double kmh, double length, double width, double height, double lane, VehicleClass vehicleClass)
    {
        float? measured = Drive(kmh, length, width, height, lane, vehicleClass, fps: 15);

        Assert.NotNull(measured);
        Assert.InRange(measured!.Value, kmh * 0.97, kmh * 1.03);
    }

    [Fact]
    public void Fast_vehicles_get_a_wider_zone_instead_of_no_speed()
    {
        // At 90 km/h a car is in the 400 px zone for only a few frames at 15 fps; the zone grows to reach enough.
        float? measured = Drive(90, 4.5, 1.8, 1.5, 4.5, VehicleClass.Car, fps: 15);

        Assert.NotNull(measured);
        Assert.InRange(measured!.Value, 87f, 93f);
    }

    /// <summary>
    /// A box-shaped vehicle driving west along the road at constant speed; its box in each frame is the bounding box
    /// of its outline as the camera sees it (edges sampled, so distortion bends them as in a real image).
    /// </summary>
    private static float? Drive(double kmh, double length, double width, double height, double lane, VehicleClass vehicleClass, double fps)
    {
        (double X, double Y)? lineFar = Camera.Project(Camera.OnRoad(0, -1).X, Camera.OnRoad(0, -1).Y, 0);
        (double X, double Y)? lineNear = Camera.Project(Camera.OnRoad(0, 7).X, Camera.OnRoad(0, 7).Y, 0);
        var line = new LineSegment(new Point2((float)lineFar!.Value.X, (float)lineFar.Value.Y), new Point2((float)lineNear!.Value.X, (float)lineNear.Value.Y));
        var speed = new SpeedSettings(0.01f, 0.01f, 0.1f, 0.9f, ZonePixels: 400, MaxZonePixels: 600, Camera: Camera);
        var engine = new CountingEngine(new CountingSettings(line, EmitDistancePixels: 100_000, EmitDelaySeconds: 0, Speed: speed));
        List<VehiclePassageEvent> events = [];
        int frame = 0;
        double metersPerFrame = kmh / 3.6 / fps;
        for (double front = 25; front > -25 - length; front -= metersPerFrame)
        {
            if (Box(front, length, width, height, lane) is { } box)
            {
                events.AddRange(engine.Process(new TrackingResult(
                    T0 + TimeSpan.FromSeconds(frame / fps), [new TrackedObject(1, box, vehicleClass, 0.9f)], [])));
            }

            frame++;
        }

        events.AddRange(engine.Process(new TrackingResult(T0 + TimeSpan.FromSeconds(frame / fps), [], [1])));
        return Assert.Single(events).SpeedKmh;
    }

    private static BoundingBox? Box(double front, double length, double width, double height, double lane)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i <= 20; i++)
        {
            for (int j = 0; j <= 4; j++)
            {
                for (int k = 0; k <= 10; k++)
                {
                    (double x, double y, double _) = Camera.OnRoad(front + (length * i / 20), lane - (width * j / 4));
                    if (Camera.Project(x, y, height * k / 10) is not { } p)
                    {
                        return null;
                    }

                    (minX, minY, maxX, maxY) = (Math.Min(minX, p.X), Math.Min(minY, p.Y), Math.Max(maxX, p.X), Math.Max(maxY, p.Y));
                }
            }
        }

        minX = Math.Max(minX, 0);
        maxX = Math.Min(maxX, 1920);
        return maxX - minX < 20 ? null : BoundingBox.FromCorners((float)minX, (float)minY, (float)maxX, (float)maxY);
    }
}
