using OpenCvSharp;

namespace TrafficCounter.Vision.Tests;

public class DiagnosticsTests
{
    [Theory]
    [InlineData(-7, "≈ 1/128 s")]
    [InlineData(-5, "≈ 1/32 s")]
    [InlineData(-1, "≈ 1/2 s")]
    [InlineData(0, "≈ 1 s")]
    public void Exposure_is_described_as_time(double exposure, string expected) =>
        Assert.Equal(expected, ExposureFormat.Describe(exposure));

    [Theory]
    [InlineData(156)] // e.g. a backend reporting absolute units
    [InlineData(-20)]
    public void Exposure_outside_log2_range_has_no_description(double exposure) =>
        Assert.Null(ExposureFormat.Describe(exposure));

    [Fact]
    public void FrameRateMeter_measures_frames_per_second()
    {
        var meter = new FrameRateMeter(window: 10);
        DateTimeOffset t0 = DateTimeOffset.UnixEpoch;
        for (int i = 0; i < 25; i++)
        {
            meter.Add(t0.AddMilliseconds(i * 40));
        }

        Assert.Equal(25, meter.FramesPerSecond, 3);
    }

    [Fact]
    public void FrameRateMeter_needs_two_frames() =>
        Assert.Equal(0, new FrameRateMeter().FramesPerSecond);

    [Fact]
    public void Uniform_image_has_its_brightness_and_no_sharpness()
    {
        using var image = new Mat(480, 640, MatType.CV_8UC3, new Scalar(100, 100, 100));
        ImageMetrics metrics = ImageMetrics.Measure(image);

        Assert.Equal(100, metrics.Brightness, 1);
        Assert.Equal(0, metrics.Sharpness, 3);
        Assert.Equal(0, metrics.Contrast, 3);
    }

    [Fact]
    public void Blurred_image_is_less_sharp_than_original()
    {
        const int width = 640;
        const int height = 480;
        using var sharp = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        for (int x = 0; x < width; x += 16)
        {
            Cv2.Rectangle(sharp, new Rect(x, 0, 8, height), Scalar.White, -1);
        }

        using var blurred = new Mat();
        Cv2.GaussianBlur(sharp, blurred, new Size(15, 15), 0);

        Assert.True(ImageMetrics.Measure(sharp).Sharpness > 4 * ImageMetrics.Measure(blurred).Sharpness);
        Assert.Equal(127.5, ImageMetrics.Measure(sharp).Contrast, 0); // half black, half white
    }
}
