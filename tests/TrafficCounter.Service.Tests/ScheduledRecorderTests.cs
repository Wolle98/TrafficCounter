using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using TrafficCounter.Vision;

namespace TrafficCounter.Service.Tests;

public sealed class ScheduledRecorderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tc-recorder-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Frames_offered_during_a_slot_end_up_in_the_video_although_it_is_opened_in_the_background()
    {
        DateTimeOffset start = DateTimeOffset.Now;
        File.WriteAllText(Path.Combine(_directory, ScheduledRecorder.ScheduleFileName),
            JsonSerializer.Serialize(new[] { new RecordingSlot(start.LocalDateTime.AddMinutes(-1), 10) }));

        using (var recorder = new ScheduledRecorder(
            new RecordingOptions { Enabled = true, Directory = _directory, Encoder = "none" }, 25, null, _directory, NullLogger.Instance))
        {
            for (int i = 0; i < 20; i++)
            {
                using var frame = new Frame(new Mat(120, 160, MatType.CV_8UC3, Scalar.All(i * 10)), start.AddMilliseconds(40 * i), i);
                recorder.Offer(frame, null);
            }
        }

        string metadata = Assert.Single(Directory.GetFiles(_directory, "*.mp4.json"));
        Assert.True(File.Exists(metadata[..^".json".Length]));
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(metadata));
        Assert.Equal(20, json.RootElement.GetProperty("Frames").GetInt64());
        Assert.Equal(0, json.RootElement.GetProperty("Dropped").GetInt64());
    }
}
