using OpenCvSharp;

namespace TrafficCounter.Vision.Tests;

public sealed class RecordingTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 18, 0, 0, TimeSpan.FromHours(2));
    private readonly string _directory = Directory.CreateTempSubdirectory("tc-recording-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>Records frames with irregular intervals; frame i is a uniform image of brightness 20 + 10 i.</summary>
    private string Record(int frames, Rect? crop = null)
    {
        string path = Path.Combine(_directory, "test.mp4");
        using var recorder = new VideoRecorder(path, new Size(320, 240), 25, crop);
        DateTimeOffset time = T0;
        for (int i = 0; i < frames; i++)
        {
            time += TimeSpan.FromMilliseconds(i % 2 == 0 ? 30 : 50);
            using var frame = new Frame(new Mat(240, 320, MatType.CV_8UC3, Scalar.All(20 + (10 * i))), time, i + 1);
            recorder.Write(frame);
        }

        return path;
    }

    [Fact]
    public async Task Sequential_playback_delivers_every_frame_with_recorded_timestamps()
    {
        string path = Record(12);
        IReadOnlyList<DateTimeOffset> recorded = FrameTimestampsFile.TryRead(path)!;

        await using var source = new FileFrameSource(path, FilePlayback.Sequential);
        source.Start();
        List<(DateTimeOffset Time, double Brightness)> frames = [];
        while (source.TryRead(TimeSpan.Zero, out Frame? frame))
        {
            using (frame)
            {
                frames.Add((frame!.Timestamp, Cv2.Mean(frame.Image).Val0));
            }
        }

        Assert.Equal(12, frames.Count);
        Assert.Equal(recorded, frames.Select(f => f.Time));
        Assert.Equal(T0.AddMilliseconds(30), frames[0].Time);
        Assert.All(frames.Zip(frames.Skip(1)), pair => Assert.True(pair.Second.Brightness > pair.First.Brightness));
        Assert.True(source.IsCompleted);
        await Assert.ThrowsAsync<EndOfStreamException>(async () => await source.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cropped_recording_stores_only_the_region()
    {
        string path = Record(3, crop: new Rect(0, 80, 320, 96));

        await using var source = new FileFrameSource(path, FilePlayback.Sequential);
        using Frame frame = await source.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new Size(320, 96), frame.Image.Size());
    }

    [Fact]
    public async Task Video_without_sidecar_uses_frame_rate_for_timestamps()
    {
        string path = Record(5);
        File.Delete(FrameTimestampsFile.PathFor(path));

        await using var source = new FileFrameSource(path, FilePlayback.Sequential, start: T0);
        using Frame first = await source.ReadAsync(TestContext.Current.CancellationToken);
        using Frame second = await source.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(T0, first.Timestamp);
        Assert.Equal(T0 + TimeSpan.FromSeconds(1.0 / 25), second.Timestamp);
    }

    [Fact]
    public async Task RealTime_playback_ends_with_end_of_stream()
    {
        string path = Record(6);

        await using var source = new FileFrameSource(path, FilePlayback.RealTime);
        source.Start();
        int received = 0;
        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
        {
            while (true)
            {
                using Frame frame = await source.ReadAsync(TestContext.Current.CancellationToken);
                received++;
            }
        });

        Assert.InRange(received, 1, 6);
        Assert.Equal(6, received + source.Statistics.Dropped);
    }
}
