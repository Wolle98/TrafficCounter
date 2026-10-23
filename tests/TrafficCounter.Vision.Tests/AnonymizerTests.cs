using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Vision.Tests;

public class AnonymizerTests
{
    /// <summary>Image with fine detail everywhere, so that pixelation is measurable as lost detail.</summary>
    private static Mat Noise()
    {
        var image = new Mat(200, 400, MatType.CV_8UC3);
        Cv2.Randu(image, new Scalar(0, 0, 0), new Scalar(255, 255, 255));
        return image;
    }

    /// <summary>Mean absolute difference between neighbouring pixels: high for detail, low after pixelation.</summary>
    private static double Detail(Mat image, Rect region)
    {
        using Mat roi = new(image, region);
        using Mat gray = new();
        Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
        using Mat shifted = new(gray, new Rect(1, 0, gray.Width - 1, gray.Height));
        using Mat original = new(gray, new Rect(0, 0, gray.Width - 1, gray.Height));
        using Mat diff = new();
        Cv2.Absdiff(shifted, original, diff);
        return Cv2.Mean(diff).Val0;
    }

    [Fact]
    public void Lower_third_of_a_vehicle_is_pixelated_the_rest_is_kept()
    {
        using Mat image = Noise();
        var car = new BoundingBox(100, 50, 180, 90); // lower third: y 110..140
        Anonymizer.Apply(image, [(car, VehicleClass.Car)], [], new Point(0, 0));

        Assert.True(Detail(image, new Rect(110, 115, 160, 20)) < 10, "number plate area keeps detail");
        Assert.True(Detail(image, new Rect(110, 55, 160, 40)) > 50, "upper part of the car lost detail");
    }

    [Fact]
    public void Upper_part_of_a_person_and_whole_private_areas_are_pixelated()
    {
        using Mat image = Noise();
        var person = new BoundingBox(300, 40, 40, 120);
        Anonymizer.Apply(image, [], [person], new Point(0, 0), [new Rect(10, 150, 60, 40)]);

        Assert.True(Detail(image, new Rect(302, 42, 36, 20)) < 10, "face area keeps detail");
        Assert.True(Detail(image, new Rect(302, 120, 36, 30)) > 50, "legs lost detail");
        Assert.True(Detail(image, new Rect(12, 152, 56, 36)) < 10, "private area keeps detail");
    }

    [Fact]
    public void Space_above_a_bicycle_is_pixelated_only_without_a_detected_rider()
    {
        var bicycle = new BoundingBox(100, 120, 100, 60); // space above: y 84..132

        using Mat alone = Noise();
        Anonymizer.Apply(alone, [(bicycle, VehicleClass.Bicycle)], [], new Point(0, 0));
        Assert.True(Detail(alone, new Rect(105, 90, 90, 25)) < 10, "head area without a rider keeps detail");

        // With the rider detected only the person's head is pixelated, not the body above the bicycle.
        using Mat ridden = Noise();
        var rider = new BoundingBox(130, 40, 40, 110); // head: y 40..64
        Anonymizer.Apply(ridden, [(bicycle, VehicleClass.Bicycle)], [rider], new Point(0, 0));
        Assert.True(Detail(ridden, new Rect(132, 42, 36, 20)) < 10, "rider's face keeps detail");
        Assert.True(Detail(ridden, new Rect(105, 90, 90, 25)) > 50, "rider's body lost detail");
    }

    [Fact]
    public void Boxes_are_shifted_by_the_origin_of_the_cropped_image()
    {
        // The image is the band starting at row 100 of the frame; the box is in frame coordinates.
        using Mat image = Noise();
        var car = new BoundingBox(100, 150, 180, 90); // lower third in the band: y 110..140
        Anonymizer.Apply(image, [(car, VehicleClass.Car)], [], new Point(0, 100));

        Assert.True(Detail(image, new Rect(110, 115, 160, 20)) < 10);
    }
}
