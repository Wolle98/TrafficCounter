using OpenCvSharp;

namespace TrafficCounter.Vision.Tests;

public class RaindropDetectorTests
{
    /// <summary>A dark night scene with a few point lights, optionally covered with faint, blurred discs like lit raindrops.</summary>
    private static Mat NightScene(int drops)
    {
        var image = new Mat(1080, 1920, MatType.CV_8UC3, new Scalar(25, 30, 28));
        var random = new Random(7);
        using var overlay = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(0));
        for (int i = 0; i < drops; i++)
        {
            Cv2.Circle(overlay, new Point(random.Next(40, 1880), random.Next(40, 1040)), random.Next(14, 22), Scalar.All(45), -1);
        }

        Cv2.GaussianBlur(overlay, overlay, new Size(0, 0), 4);
        Cv2.Add(image, overlay, image);

        // Street lamp and two lit windows: present on dry nights too.
        Cv2.Circle(image, new Point(840, 100), 12, Scalar.All(250), -1);
        Cv2.Rectangle(image, new Rect(350, 330, 20, 30), Scalar.All(200), -1);
        Cv2.Rectangle(image, new Rect(1500, 300, 20, 30), Scalar.All(200), -1);
        return image;
    }

    [Fact]
    public void Drops_lit_at_night_give_many_discs()
    {
        using Mat rainy = NightScene(drops: 80);
        Assert.True(RaindropDetector.CountDiscs(rainy) >= 35);
    }

    [Fact]
    public void A_dry_night_gives_few()
    {
        using Mat dry = NightScene(drops: 0);
        Assert.True(RaindropDetector.CountDiscs(dry) < 10);
    }
}
