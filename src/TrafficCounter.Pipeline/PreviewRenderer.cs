using OpenCvSharp;
using TrafficCounter.Vision;
using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Pipeline;

/// <summary>Debug overlay: band, counting line, detections, tracks and counters. Display only – nothing is saved.</summary>
public sealed class PreviewRenderer(SiteOptions site, int displayWidth = 1280)
{
    private static readonly Scalar BandColor = new(255, 200, 0);
    private static readonly Scalar LineColor = new(0, 220, 255);
    private static readonly Scalar DetectionColor = new(160, 160, 160);
    private static readonly Scalar TrackColor = new(80, 220, 80);
    private static readonly Scalar TrailerColor = new(0, 140, 255);
    private static readonly Scalar IgnoreColor = new(128, 128, 128);
    private int _countA;
    private int _countB;

    public Mat Render(Mat frame, FrameResult result, IEnumerable<string> statusLines)
    {
        foreach (VehiclePassageEvent passage in result.Passages)
        {
            if (passage.Direction == TravelDirection.A)
            {
                _countA++;
            }
            else
            {
                _countB++;
            }
        }

        using Mat canvas = frame.Clone();
        Cv2.Rectangle(canvas, result.Band, BandColor, 2);
        foreach (RelativeRect zone in site.IgnoreZones)
        {
            DrawIgnoreZone(canvas, zone);
        }

        Cv2.Line(canvas, ToPoint(result.Line.Start), ToPoint(result.Line.End), LineColor, 3);

        foreach (Detection detection in result.Detections)
        {
            Cv2.Rectangle(canvas, ToRect(detection.Box), DetectionColor, 1);
        }

        foreach (TrackedObject track in result.Tracking.Active)
        {
            Rect box = ToRect(track.Box);
            bool trailer = result.TrailerTrackIds?.Contains(track.TrackId) == true;
            Scalar color = trailer ? TrailerColor : TrackColor;
            Cv2.Rectangle(canvas, box, color, 2);
            Point2 bottom = track.Box.BottomCenter;
            Cv2.Circle(canvas, ToPoint(bottom), 5, color, -1);
            string label = trailer ? $"#{track.TrackId} trailer" : $"#{track.TrackId} {track.Class} {track.Confidence:0.00}";
            Label(canvas, label, new Point(box.X, box.Y - 6), 0.6);
        }

        Mat display = new();
        double scale = Math.Min(1.0, (double)displayWidth / canvas.Width);
        Cv2.Resize(canvas, display, new Size(0, 0), scale, scale, InterpolationFlags.Area);

        int y = 28;
        foreach (string line in statusLines.Prepend($"{site.DirectionAName} (A): {_countA}   {site.DirectionBName} (B): {_countB}"))
        {
            Label(display, line, new Point(10, y), 0.65);
            y += 26;
        }

        return display;
    }

    /// <summary>Grey hatched rectangle: detections with their ground point here are dropped.</summary>
    private static void DrawIgnoreZone(Mat image, RelativeRect zone)
    {
        var rect = new Rect(
            (int)(zone.X * image.Width), (int)(zone.Y * image.Height), (int)(zone.Width * image.Width), (int)(zone.Height * image.Height));
        rect &= new Rect(0, 0, image.Width, image.Height);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using Mat region = new(image, rect);
        for (int d = 0; d < rect.Width + rect.Height; d += 16)
        {
            Cv2.Line(region, new Point(d, 0), new Point(d - rect.Height, rect.Height), IgnoreColor, 1);
        }

        Cv2.Rectangle(image, rect, IgnoreColor, 2);
    }

    private static void Label(Mat image, string text, Point origin, double size)
    {
        Cv2.PutText(image, Overlay.Ascii(text), origin + new Point(1, 1), HersheyFonts.HersheySimplex, size, Scalar.Black, 3);
        Cv2.PutText(image, Overlay.Ascii(text), origin, HersheyFonts.HersheySimplex, size, Scalar.White, 1);
    }

    private static Point ToPoint(Point2 point) => new((int)Math.Round(point.X), (int)Math.Round(point.Y));

    private static Rect ToRect(BoundingBox box) => new((int)box.Left, (int)box.Top, (int)box.Width, (int)box.Height);
}
