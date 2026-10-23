using OpenCvSharp;

namespace TrafficCounter.Pipeline.Tests;

public class ImageEnhancerTests
{
    [Fact]
    public void Static_highlights_fade_while_moving_objects_stay()
    {
        var band = new Rect(0, 0, 320, 120);
        using var enhancer = new ImageEnhancer(new EnhancementOptions { Mode = EnhancementMode.StaticHighlights });
        byte staticSpot = 0, movingSpot = 0;
        for (int i = 0; i < 300; i++) // 20 s at 15 fps
        {
            using var frame = new Mat(120, 320, MatType.CV_8UC3, Scalar.All(30));
            Cv2.Circle(frame, new Point(60, 60), 8, Scalar.All(200), -1);                   // lit drop, always there
            int x = 100 + (i % 20 * 10);
            Cv2.Rectangle(frame, new Rect(x, 50, 20, 20), Scalar.All(200), -1);           // vehicle light, moving
            Mat result = enhancer.Enhance(frame, band);
            staticSpot = result.At<Vec3b>(60, 60).Item0;
            movingSpot = result.At<Vec3b>(60, x + 10).Item0;
        }

        Assert.True(staticSpot < 120, $"static spot still {staticSpot}");
        Assert.True(movingSpot > 150, $"moving object only {movingSpot}");
    }

    [Fact]
    public void Grayscale_makes_the_band_grey_and_keeps_its_brightness()
    {
        using var frame = new Mat(120, 160, MatType.CV_8UC3, new Scalar(40, 90, 160)); // BGR, warm colour
        var band = new Rect(0, 30, 160, 60);
        using var enhancer = new ImageEnhancer(new EnhancementOptions { Mode = EnhancementMode.Grayscale });

        Mat result = enhancer.Enhance(frame, band);

        Vec3b pixel = result.At<Vec3b>(50, 80);
        Assert.Equal(pixel.Item0, pixel.Item1);
        Assert.Equal(pixel.Item1, pixel.Item2);
        Assert.InRange(pixel.Item0, 95, 105); // luma of the colour: 0.114*40 + 0.587*90 + 0.299*160 = 105
        Assert.Equal(160, frame.At<Vec3b>(50, 80).Item2); // the original frame keeps its colour
    }

    [Fact]
    public void Only_the_band_is_enhanced_and_the_frame_stays_untouched()
    {
        using var frame = new Mat(120, 160, MatType.CV_8UC3);
        Cv2.Randu(frame, Scalar.All(0), Scalar.All(255));
        using Mat original = frame.Clone();
        var band = new Rect(20, 40, 100, 50);
        using var enhancer = new ImageEnhancer(new EnhancementOptions { Mode = EnhancementMode.Gamma, Gamma = 0.5 });

        for (int i = 0; i < 2; i++) // the buffer is reused
        {
            Mat result = enhancer.Enhance(frame, band);

            using Mat bandBefore = new(original, band);
            using Mat bandAfter = new(result, band);
            Assert.Equal(frame.Size(), result.Size());
            byte before = bandBefore.At<Vec3b>(10, 10).Item0;
            byte expected = (byte)Math.Round(255 * Math.Pow(before / 255.0, 0.5));
            Assert.Equal(expected, bandAfter.At<Vec3b>(10, 10).Item0);
            using Mat difference = new();
            Cv2.Absdiff(frame, original, difference);
            Assert.Equal(0, Cv2.CountNonZero(difference.Reshape(1)));
        }
    }
}
