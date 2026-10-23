using System.Globalization;
using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;
using TrafficCounter.Pipeline;
using TrafficCounter.Tracking;
using TrafficCounter.Vision;

namespace TrafficCounter.Regression;

/// <summary>
/// Estimates the speed scale of the road (ADR-0015) without measuring on the street: near the counting line the
/// camera looks at the side of passing cars, so the box width is about the car's length. Cars average about
/// 4.5 m; per lane the median box width of many cars therefore gives meters per pixel. The width grows towards
/// the camera, so it is fitted as a straight line over the position along the counting line and evaluated at
/// both kerbs. Accurate to roughly ±10 %; measured points or test drives refine it later.
/// </summary>
internal static class SpeedCalibration
{
    /// <summary>Only boxes whose bottom center is this close to the line (fraction of the image width).</summary>
    private const float LineZone = 0.04f;

    /// <summary>Boxes closer to the image edge than this (pixels) may be cut off.</summary>
    private const float EdgeMargin = 8f;

    public static async Task<int> RunAsync(
        IReadOnlyList<string> videos, Func<string, SiteOptions> siteFor, IObjectDetector detector, TrackerSettings trackerSettings,
        ProcessingOptions processing, float carLength)
    {
        List<(float Position, float Width)> cars = [];
        SiteOptions? reference = null;
        int imageWidth = 0;
        foreach (string video in videos)
        {
            SiteOptions site = siteFor(video);
            reference ??= site;
            (List<(float Position, float Width)> measured, int width) = await CollectAsync(video, site, detector, trackerSettings, processing);
            cars.AddRange(measured);
            imageWidth = width;
            Console.WriteLine($"{Path.GetFileName(video),-28} {measured.Count} cars measured");
        }

        if (reference?.FarKerb is not float farKerb || reference.NearKerb is not float nearKerb)
        {
            Console.Error.WriteLine("The configuration has no kerbs; calibrate them first (Calibrator, key k).");
            return 2;
        }

        if (cars.Count < 20)
        {
            Console.Error.WriteLine($"Only {cars.Count} cars measured; at least 20 are needed.");
            return 1;
        }

        // Straight line through width over position, refitted without the 15 % largest residuals
        // (vans and estate cars, partly hidden cars, boxes spanning two vehicles).
        (double slope, double intercept) = Fit(cars);
        List<(float Position, float Width)> inliers = [.. cars
            .OrderBy(c => Math.Abs(c.Width - (intercept + (slope * c.Position))))
            .Take((int)(cars.Count * 0.85))];
        (slope, intercept) = Fit(inliers);

        double farWidth = intercept + (slope * farKerb);
        double nearWidth = intercept + (slope * nearKerb);
        double far = carLength / farWidth * imageWidth;
        double near = carLength / nearWidth * imageWidth;

        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{cars.Count} cars, {inliers.Count} used. Car box width at the far kerb {farWidth:0} px, at the near kerb {nearWidth:0} px."));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"With an average car length of {carLength:0.0} m, add to the site configuration (TrafficCounter:Site):"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  \"FarKerbMetersPerImageWidth\": {far:0.0},"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  \"NearKerbMetersPerImageWidth\": {near:0.0}"));
        return 0;
    }

    private static async Task<(List<(float Position, float Width)> Cars, int ImageWidth)> CollectAsync(
        string video, SiteOptions site, IObjectDetector detector, TrackerSettings trackerSettings, ProcessingOptions processing)
    {
        using var pipeline = new CountingPipeline(detector, new ByteTracker(trackerSettings), site, processing);
        Dictionary<int, List<(float Position, float Width)>> perTrack = [];
        int imageWidth = 0;
        await using (var source = new FileFrameSource(video, FilePlayback.Sequential))
        {
            while (source.TryRead(TimeSpan.Zero, out Frame? frame))
            {
                using (frame)
                {
                    if (!pipeline.ShouldProcess(frame!.Timestamp))
                    {
                        continue;
                    }

                    Size size = frame.Image.Size();
                    imageWidth = size.Width;
                    LineSegment line = site.Line(size);
                    FrameResult result = pipeline.Process(frame);
                    IReadOnlyList<TrackedObject> active = result.Tracking.Active;
                    foreach (TrackedObject obj in active)
                    {
                        BoundingBox box = obj.Box;
                        Point2 ground = box.BottomCenter;
                        if (obj.Class != VehicleClass.Car || obj.Confidence < 0.6f
                            || Math.Abs(line.SignedDistance(ground)) > LineZone * size.Width
                            || box.Left < EdgeMargin || box.Right > size.Width - EdgeMargin
                            || active.Any(other => other.TrackId != obj.TrackId && Overlaps(box, other.Box)))
                        {
                            continue;
                        }

                        if (!perTrack.TryGetValue(obj.TrackId, out List<(float, float)>? samples))
                        {
                            perTrack[obj.TrackId] = samples = [];
                        }

                        samples.Add((line.Project(ground), box.Width));
                    }
                }
            }
        }

        // One value per car (median), so slow cars with many frames do not dominate.
        List<(float Position, float Width)> cars = [.. perTrack.Values.Where(s => s.Count >= 3).Select(s =>
        {
            List<float> widths = [.. s.Select(v => v.Width).Order()];
            List<float> positions = [.. s.Select(v => v.Position).Order()];
            return (positions[positions.Count / 2], widths[widths.Count / 2]);
        })];
        return (cars, imageWidth);
    }

    private static bool Overlaps(BoundingBox a, BoundingBox b) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    private static (double Slope, double Intercept) Fit(IReadOnlyList<(float Position, float Width)> points)
    {
        double meanP = points.Average(p => p.Position);
        double meanW = points.Average(p => p.Width);
        double covariance = points.Sum(p => (p.Position - meanP) * (p.Width - meanW));
        double variance = points.Sum(p => (p.Position - meanP) * (p.Position - meanP));
        double slope = variance > 0 ? covariance / variance : 0;
        return (slope, meanW - (slope * meanP));
    }
}
