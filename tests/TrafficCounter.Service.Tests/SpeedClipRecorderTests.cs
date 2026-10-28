using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Vision;

namespace TrafficCounter.Service.Tests;

public sealed class SpeedClipRecorderTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 12, 0, 0, TimeSpan.FromHours(1));

    // 45–55 km/h for every class but cars, from 55 km/h for all.
    private static readonly List<SpeedClipRule> Rules =
    [
        new() { MinKmh = 45, BelowKmh = 55, ExceptClasses = [VehicleClass.Car] },
        new() { MinKmh = 55 },
    ];

    private readonly string _directory = Directory.CreateTempSubdirectory("tc-clips-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(VehicleClass.Truck, 50f, true)]
    [InlineData(VehicleClass.Car, 50f, false)]
    [InlineData(VehicleClass.Car, 55f, true)]
    [InlineData(VehicleClass.Van, 44.9f, false)]
    [InlineData(VehicleClass.Motorcycle, 45f, true)]
    [InlineData(VehicleClass.Car, 72f, true)]
    public void The_rules_select_conspicuous_speeds(VehicleClass vehicleClass, float kmh, bool recorded) =>
        Assert.Equal(recorded, Rules.Any(r => r.Matches(vehicleClass, kmh)));

    [Fact]
    public void A_matching_passage_gets_a_clip_around_its_crossing_with_metadata()
    {
        using (var clips = new SpeedClipRecorder(
            new SpeedClipOptions { Until = T0.AddDays(1), Rules = Rules, SecondsBefore = 2, SecondsAfter = 1 },
            new RecordingOptions { Directory = _directory, Encoder = "none" }, null, _directory, NullLogger.Instance))
        {
            for (int i = 0; i < 150; i++)
            {
                using var frame = new Frame(new Mat(120, 160, MatType.CV_8UC3, Scalar.All(i % 255)), T0.AddSeconds(i / 15.0), i);
                clips.Offer(frame);
                if (i == 120)
                {
                    // Emitted 3 s after crossing at 5 s; a car at 50 km/h is not recorded.
                    clips.OnPassage(Passage(VehicleClass.Truck, 52, T0.AddSeconds(5)));
                    clips.OnPassage(Passage(VehicleClass.Car, 50, T0.AddSeconds(5.5)));
                }

                Thread.Sleep(1); // the encoder thread keeps up, as at 15 fps in operation
            }
        }

        string video = Assert.Single(Directory.GetFiles(_directory, "*.mp4", SearchOption.AllDirectories));
        Assert.Contains("Truck-52kmh", Path.GetFileName(video));
        Assert.Equal(Path.Combine(_directory, "speed"), Path.GetDirectoryName(video));
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(video + ".json"));
        Assert.Equal("Truck", json.RootElement.GetProperty("Passage").GetProperty("Class").GetString());
        Assert.InRange(json.RootElement.GetProperty("Passage").GetProperty("OffsetSeconds").GetDouble(), 1.9, 2.1);
        Assert.InRange(json.RootElement.GetProperty("Frames").GetInt32(), 40, 46);
        Assert.True(File.Exists(FrameTimestampsFile.PathFor(video)));
    }

    [Fact]
    public void Only_active_with_rules_and_an_end_at_most_30_days_ahead()
    {
        Assert.False(new SpeedClipOptions { Rules = Rules }.IsActive(T0));
        Assert.True(new SpeedClipOptions { Rules = Rules, Until = T0.AddDays(30) }.IsActive(T0));
        Assert.False(new SpeedClipOptions { Rules = Rules, Until = T0.AddDays(31) }.IsActive(T0));
        Assert.False(new SpeedClipOptions { Rules = Rules, Until = T0.AddMinutes(-1) }.IsActive(T0));
        Assert.False(new SpeedClipOptions { Until = T0.AddDays(1) }.IsActive(T0));
    }

    [Fact]
    public void No_more_clips_than_the_daily_limit()
    {
        using (var clips = new SpeedClipRecorder(
            new SpeedClipOptions { Until = T0.AddDays(1), Rules = Rules, SecondsBefore = 1, SecondsAfter = 0.5, DailyLimit = 1 },
            new RecordingOptions { Directory = _directory, Encoder = "none" }, null, _directory, NullLogger.Instance))
        {
            for (int i = 0; i < 90; i++)
            {
                using var frame = new Frame(new Mat(120, 160, MatType.CV_8UC3, Scalar.All(0)), T0.AddSeconds(i / 15.0), i);
                clips.Offer(frame);
                if (i == 60)
                {
                    clips.OnPassage(Passage(VehicleClass.Car, 70, T0.AddSeconds(2)));
                    clips.OnPassage(Passage(VehicleClass.Car, 80, T0.AddSeconds(3)));
                }

                Thread.Sleep(1);
            }
        }

        Assert.Single(Directory.GetFiles(_directory, "*.mp4", SearchOption.AllDirectories));
    }

    private static VehiclePassageEvent Passage(VehicleClass vehicleClass, float kmh, DateTimeOffset at) =>
        new(at, TravelDirection.A, vehicleClass, 0.9f, kmh, 1, 30, 0.5f);
}
