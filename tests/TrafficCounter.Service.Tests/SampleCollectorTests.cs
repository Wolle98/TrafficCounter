using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;
using TrafficCounter.Core.Operations;
using TrafficCounter.Pipeline;
using TrafficCounter.Vision;

namespace TrafficCounter.Service.Tests;

public sealed class SampleCollectorTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(2));
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tc-samples-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private SampleCollector Collector(int dailyLimit = 200, double randomShare = 0) =>
        SampleCollector.Create(
            new SampleCollectionOptions { Until = T0.AddDays(10), Directory = _directory, DailyLimit = dailyLimit, RandomShare = randomShare },
            _directory, T0, NullLogger.Instance)!;

    /// <summary>Drives one vehicle of the given class from x = 100 to 540 across the line at x = 320.</summary>
    private static void Drive(SampleCollector collector, int trackId, VehicleClass vehicleClass, DateTimeOffset start, Action? personsAsked = null)
    {
        var line = new LineSegment(new Point2(320, 0), new Point2(320, 360));
        for (int i = 0; i <= 22; i++)
        {
            using Mat image = new(360, 640, MatType.CV_8UC3, Scalar.Gray);
            DateTimeOffset at = start.AddSeconds(i * 0.1);
            float x = 100 + (20 * i);
            var box = new BoundingBox(x - 60, 200, 120, 60);
            var tracking = new TrackingResult(at, [new TrackedObject(trackId, box, vehicleClass, 0.9f)], []);
            List<VehiclePassageEvent> passages = i == 22
                ? [new VehiclePassageEvent(start.AddSeconds(1.1), TravelDirection.B, vehicleClass, 1f, 30f, trackId, 23, 0.5f)]
                : [];
            var result = new FrameResult(new Rect(0, 0, 640, 360), line, [new Core.Detection(box, vehicleClass, 0.9f)], tracking, passages, TimeSpan.Zero);
            using var frame = new Frame(image.Clone(), at, i);
            collector.Observe(frame, result, () =>
            {
                personsAsked?.Invoke();
                return [];
            }, [], LightingCondition.Day);
        }
    }

    private string[] Samples()
    {
        // Files are written in the background.
        for (int i = 0; i < 50 && Directory.GetFiles(_directory, "sample.json", SearchOption.AllDirectories).Length == 0; i++)
        {
            Thread.Sleep(50);
        }

        Thread.Sleep(100);
        return Directory.GetDirectories(_directory, "*", SearchOption.AllDirectories).Where(d => File.Exists(Path.Combine(d, "sample.json"))).ToArray();
    }

    [Fact]
    public void Off_without_or_with_a_past_end_date_and_at_most_30_days()
    {
        Assert.Null(SampleCollector.Create(new SampleCollectionOptions(), _directory, T0, NullLogger.Instance));
        Assert.Null(SampleCollector.Create(new SampleCollectionOptions { Until = T0.AddMinutes(-1) }, _directory, T0, NullLogger.Instance));
        SampleCollector collector = SampleCollector.Create(new SampleCollectionOptions { Until = T0.AddDays(90), Directory = _directory }, _directory, T0, NullLogger.Instance)!;
        Assert.Equal(T0.AddDays(30), collector.Until);
    }

    [Fact]
    public void Rare_class_is_stored_with_three_images_and_reasons()
    {
        Drive(Collector(), 7, VehicleClass.Bus, T0);

        string sample = Assert.Single(Samples());
        Assert.True(File.Exists(Path.Combine(sample, "entry.jpg")));
        Assert.True(File.Exists(Path.Combine(sample, "line.jpg")));
        Assert.True(File.Exists(Path.Combine(sample, "exit.jpg")));
        Assert.Contains("rare class Bus", File.ReadAllText(Path.Combine(sample, "sample.json")));
    }

    [Fact]
    public void Entry_and_exit_images_are_taken_at_a_fixed_distance_from_the_line()
    {
        // Band 640 px wide: 15 % is 96 px. The vehicle moves 20 px per 0.1 s from x = 100; the line is at x = 320.
        // Entry: first position within 96 px before the line (x = 240, 0.7 s); exit: first one 96 px past it
        // (x = 420, 1.6 s), not the end of the track (x = 540, 2.2 s).
        Drive(Collector(), 7, VehicleClass.Bus, T0);

        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Assert.Single(Samples()), "sample.json")));
        Dictionary<string, DateTimeOffset> times = json.RootElement.GetProperty("Images").EnumerateArray()
            .ToDictionary(i => i.GetProperty("File").GetString()!, i => i.GetProperty("Time").GetDateTimeOffset());
        Assert.Equal(T0.AddSeconds(0.7), times["entry.jpg"]);
        Assert.Equal(T0.AddSeconds(1.1), times["line.jpg"]);
        Assert.Equal(T0.AddSeconds(1.6), times["exit.jpg"]);
    }

    [Fact]
    public void People_are_asked_for_only_when_an_image_is_taken()
    {
        // A separate person model must not run on every frame (23 frames here), only for the few images taken.
        int asked = 0;
        Drive(Collector(), 7, VehicleClass.Bus, T0, () => asked++);

        Assert.InRange(asked, 3, 8);
        Assert.Single(Samples()); // waits for the background writer before the folder is removed
    }

    [Fact]
    public void Bicycles_count_as_a_rare_class()
    {
        Drive(Collector(), 7, VehicleClass.Bicycle, T0);

        Assert.Contains("rare class Bicycle", File.ReadAllText(Path.Combine(Assert.Single(Samples()), "sample.json")));
    }

    [Fact]
    public void Confident_ordinary_car_is_not_stored()
    {
        Drive(Collector(), 7, VehicleClass.Car, T0);
        Thread.Sleep(300);
        Assert.False(Directory.EnumerateFiles(_directory, "*.jpg", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public void Daily_limit_is_spread_over_the_day()
    {
        Assert.Equal(20, SampleCollector.Allowance(200, 0.1, TimeSpan.Zero));
        Assert.Equal(110, SampleCollector.Allowance(200, 0.1, TimeSpan.FromHours(12)));
        Assert.Equal(200, SampleCollector.Allowance(200, 0.1, new TimeSpan(23, 59, 59)));
        Assert.Equal(1, SampleCollector.Allowance(1, 0.1, TimeSpan.Zero));
    }

    [Fact]
    public void Daily_limit_is_kept()
    {
        SampleCollector collector = Collector(dailyLimit: 1);
        Drive(collector, 1, VehicleClass.Truck, T0);
        Drive(collector, 2, VehicleClass.Truck, T0.AddSeconds(10));
        Assert.Single(Samples());
    }
}
