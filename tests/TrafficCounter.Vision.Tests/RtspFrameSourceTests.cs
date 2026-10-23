using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace TrafficCounter.Vision.Tests;

public sealed class RtspFrameSourceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tc-rtsp-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Credentials_are_added_for_connecting_and_hidden_in_logs()
    {
        var options = new RtspCameraOptions { Url = "rtsp://192.168.1.20:554/h264Preview_01_main", UserName = "admin", Password = "p@ss:w/rd" };

        Assert.Equal("rtsp://admin:p%40ss%3Aw%2Frd@192.168.1.20:554/h264Preview_01_main", options.ConnectionUrl());
        Assert.Equal("rtsp://192.168.1.20:554/h264Preview_01_main", options.DisplayUrl());
        Assert.DoesNotContain("p@ss", new RtspFrameSource(options, NullLogger<RtspFrameSource>.Instance).Description);
    }

    [Fact]
    public void Url_without_user_is_used_unchanged()
    {
        var options = new RtspCameraOptions { Url = "rtsp://camera.local/stream" };
        Assert.Equal("rtsp://camera.local/stream", options.ConnectionUrl());
    }

    [Fact]
    public void Tool_options_refuse_credentials_in_the_url() =>
        Assert.Throws<ArgumentException>(() => RtspCameraOptions.ForTool("rtsp://admin:secret@192.168.1.64/Streaming/Channels/101", null));

    [Fact]
    public void Tool_options_refuse_other_schemes() =>
        Assert.Throws<ArgumentException>(() => RtspCameraOptions.ForTool("http://192.168.1.64/", null));

    [Fact]
    public void Tool_options_take_the_password_from_the_environment()
    {
        string? before = Environment.GetEnvironmentVariable(RtspCameraOptions.PasswordVariable);
        try
        {
            Environment.SetEnvironmentVariable(RtspCameraOptions.PasswordVariable, "from-env");
            RtspCameraOptions options = RtspCameraOptions.ForTool("rtsp://192.168.1.64:554/Streaming/Channels/101", "admin", fps: 25);

            Assert.Equal(("admin", "from-env", 25.0), (options.UserName, options.Password, options.Fps));
            Assert.Equal("rtsp://192.168.1.64:554/Streaming/Channels/101", options.DisplayUrl());
        }
        finally
        {
            Environment.SetEnvironmentVariable(RtspCameraOptions.PasswordVariable, before);
        }
    }

    [Fact]
    public async Task Stream_is_read_through_ffmpeg_and_reopened_when_it_ends()
    {
        // A video file stands in for the stream: FFmpeg opens both the same way, and the end of the file
        // behaves like a broken stream, which must trigger a reconnect.
        string path = Path.Combine(_directory, "stream.mp4");
        using (var recorder = new VideoRecorder(path, new Size(160, 120), 25))
        {
            for (int i = 0; i < 10; i++)
            {
                using var frame = new Frame(new Mat(120, 160, MatType.CV_8UC3, Scalar.All(i * 20)), DateTimeOffset.Now, i);
                recorder.Write(frame);
            }
        }

        await using var source = new RtspFrameSource(new RtspCameraOptions { Url = path }, NullLogger<RtspFrameSource>.Instance);
        source.Start();

        Assert.True(source.TryRead(TimeSpan.FromSeconds(10), out Frame? first));
        first!.Dispose();
        for (int i = 0; i < 300 && source.Statistics.Reconnects == 0; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(source.Statistics.Reconnects >= 1);
    }
}
