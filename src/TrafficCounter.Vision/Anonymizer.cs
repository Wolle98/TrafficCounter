using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Vision;

/// <summary>
/// Makes faces and number plates unrecognisable before a training sample is stored (ADR-0026): coarse pixelation of
/// the lower third of every vehicle box (number plates), the upper fifth of every person box (faces) and, only if no
/// person was detected on it, the space above every bicycle or motorcycle (the rider's head). Only the head is
/// pixelated, not the rider's body: the rider belongs to the two-wheeler's box (ADR-0039), and the live images the
/// model sees are not pixelated. Shape and size of the vehicles stay recognisable for classification.
/// </summary>
public static class Anonymizer
{
    /// <summary>Side length of the pixel blocks; at the camera distance a face or a plate becomes a few blocks.</summary>
    public const int BlockSize = 12;

    /// <param name="image">Image to change in place, e.g. the evaluation band.</param>
    /// <param name="origin">Position of <paramref name="image"/> in the frame the boxes refer to.</param>
    /// <param name="areas">Areas pixelated completely, e.g. driveways with parked cars (ignore zones), in frame coordinates.</param>
    public static void Apply(
        Mat image, IEnumerable<(BoundingBox Box, VehicleClass Class)> vehicles, IEnumerable<BoundingBox> persons, Point origin, IEnumerable<Rect>? areas = null)
    {
        foreach (Rect area in areas ?? [])
        {
            Pixelate(image, origin, area.X, area.Y, area.Width, area.Height);
        }

        IReadOnlyCollection<BoundingBox> personList = [.. persons];
        foreach ((BoundingBox box, VehicleClass vehicleClass) in vehicles)
        {
            Pixelate(image, origin, box.Left, box.Top + (box.Height * 2 / 3), box.Width, box.Height / 3);
            if (vehicleClass is VehicleClass.Bicycle or VehicleClass.Motorcycle && !personList.Any(person => Rides(person, box)))
            {
                Pixelate(image, origin, box.Left, box.Top - (box.Height * 0.6f), box.Width, box.Height * 0.8f);
            }
        }

        foreach (BoundingBox person in personList)
        {
            // A little wider than the box, heads are often cut off at the sides.
            float margin = person.Width * 0.1f;
            Pixelate(image, origin, person.Left - margin, person.Top, person.Width + (2 * margin), Math.Max(person.Height / 5, person.Width * 0.6f));
        }
    }

    /// <summary>A person detected on or above the two-wheeler: overlapping it horizontally and reaching down into it.</summary>
    private static bool Rides(BoundingBox person, BoundingBox vehicle)
    {
        float overlap = Math.Min(person.Right, vehicle.Right) - Math.Max(person.Left, vehicle.Left);
        return overlap >= person.Width * 0.5f && person.Bottom >= vehicle.Top && person.Top < vehicle.Top + (vehicle.Height / 2);
    }

    private static void Pixelate(Mat image, Point origin, float left, float top, float width, float height)
    {
        var rect = new Rect(
            (int)Math.Floor(left) - origin.X, (int)Math.Floor(top) - origin.Y, (int)Math.Ceiling(width) + 1, (int)Math.Ceiling(height) + 1);
        rect &= new Rect(0, 0, image.Width, image.Height);
        if (rect.Width < 2 || rect.Height < 2)
        {
            return;
        }

        using Mat region = new(image, rect);
        using Mat small = new();
        Cv2.Resize(region, small, new Size(Math.Max(1, rect.Width / BlockSize), Math.Max(1, rect.Height / BlockSize)), 0, 0, InterpolationFlags.Area);
        Cv2.Resize(small, region, rect.Size, 0, 0, InterpolationFlags.Nearest);
    }
}
