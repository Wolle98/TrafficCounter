using OpenCvSharp;

namespace TrafficCounter.Vision.Tests;

public sealed class RecordingPolicyTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("tc-policy-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Recording_into_the_public_repository_is_refused()
    {
        string repo = Directory.CreateDirectory(Path.Combine(_root, "TrafficCounter")).FullName;
        File.WriteAllText(Path.Combine(repo, RecordingPolicy.PublicRepositoryMarker), "");

        Assert.Throws<ArgumentException>(() =>
            RecordingPolicy.EnsureNotInPublicRepository(Path.Combine(repo, "docs", "sub", "video.mp4")));
    }

    [Fact]
    public void Recording_elsewhere_is_allowed() =>
        RecordingPolicy.EnsureNotInPublicRepository(Path.Combine(_root, "TrafficCounterPrivate", "recordings", "video.mp4"));

    [Fact]
    public void Band_is_parsed() => Assert.Equal((0.32, 0.78), RecordingPolicy.ParseBand("0.32-0.78"));

    [Theory]
    [InlineData("0.8-0.3")]
    [InlineData("0.3")]
    [InlineData("0.3-1.2")]
    [InlineData("a-b")]
    public void Invalid_band_is_rejected(string value) =>
        Assert.Throws<ArgumentException>(() => RecordingPolicy.ParseBand(value));

    [Fact]
    public void Band_rect_spans_full_width_with_even_height()
    {
        Rect rect = RecordingPolicy.BandRect(new Size(1920, 1080), 0.32, 0.781);
        // 0.32 * 1080 = 345.6 -> 346, 0.781 * 1080 = 843.5 -> 843, height 497 -> 496 (even).
        Assert.Equal(new Rect(0, 346, 1920, 496), rect);
    }
}
