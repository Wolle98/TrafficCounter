using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Core.Operations;
using TrafficCounter.Evaluation;
using TrafficCounter.Pipeline;
using TrafficCounter.Tracking;
using TrafficCounter.Vision;

namespace TrafficCounter.Regression;

/// <summary>
/// Turns hand-counted passages of one class into training samples in the same form as the service's collection
/// (ADR-0026): three anonymised images of the band (before, at and after the counting line) and sample.json with the
/// detector's boxes as labelling proposals. This reaches exactly the vehicles the model misses, which the service can
/// never collect because it only sees what it tracks (e.g. bicycles and e-scooters at night).
/// </summary>
internal static class SampleExtraction
{
    private static readonly (string Name, double Seconds)[] Views = [("entry", -0.6), ("line", 0), ("exit", 0.6)];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static int Run(
        IReadOnlyList<string> videos, Func<string, SiteOptions> siteFor, IObjectDetector detector, TrackerSettings trackerSettings,
        VehicleClass vehicleClass, string inbox, bool recut = false, Func<string, List<GroundTruthEntry>>? passagesFor = null, string? reason = null)
    {
        int written = 0, existing = 0, reclassified = 0, recutCount = 0;
        foreach (string video in videos)
        {
            // Hand-counted passages of the class, or a given list (e.g. passages only a stronger model counted).
            List<GroundTruthEntry> passages = passagesFor?.Invoke(video)
                ?? [.. GroundTruthFile.Read(GroundTruthFile.PathFor(video)).Where(e => e.Class == vehicleClass)];
            if (passages.Count == 0)
            {
                continue;
            }

            SiteOptions site = siteFor(video);
            IReadOnlyList<DateTimeOffset>? timestamps = FrameTimestampsFile.TryRead(video);
            using var capture = new VideoCapture(video);
            double fps = capture.Fps > 0 ? capture.Fps : 25;
            DateTimeOffset start = timestamps is { Count: > 0 } ? timestamps[0] : File.GetCreationTime(video);
            using var pipeline = new CountingPipeline(detector, new ByteTracker(trackerSettings), site, new ProcessingOptions());
            var lighting = new LightingClassifier(site.Latitude, site.Longitude);
            int videoWritten = 0;
            for (int index = 0; index < passages.Count; index++)
            {
                GroundTruthEntry passage = passages[index];
                DateTimeOffset at = start + passage.Offset;
                string folder = Path.Combine(inbox, $"{at.LocalDateTime:yyyy-MM-dd}", $"{at.LocalDateTime:HHmmss}-gt{index}");
                // The index counts within the class, so a passage extracted before under another class (a bicycle
                // later found to be an e-scooter) has another folder name: look for it by recording and time.
                if (FindExisting(Path.GetDirectoryName(folder)!, $"{at.LocalDateTime:HHmmss}-gt*", Path.GetFileName(video), at) is { } previous)
                {
                    if (!recut && passagesFor is not null)
                    {
                        existing++;
                        continue; // already a sample of this passage; mined passages never change a class
                    }

                    if (!recut)
                    {
                        existing++;
                        if (Reclassify(previous, vehicleClass, passage.Note))
                        {
                            reclassified++;
                            Console.WriteLine($"  {Path.GetFileName(previous)}: class changed to {vehicleClass}");
                        }

                        continue;
                    }

                    // Cut again in place (e.g. after a geometry fix): the folder name stays, so tasks that refer
                    // to its images keep working.
                    folder = previous;
                    recutCount++;
                }
                else if (recut)
                {
                    continue; // only existing samples are cut again
                }

                List<object> images = [];
                Directory.CreateDirectory(folder);
                foreach ((string name, double seconds) in Views)
                {
                    TimeSpan offset = passage.Offset + TimeSpan.FromSeconds(seconds);
                    capture.Set(VideoCaptureProperties.PosFrames, FrameAt(offset, timestamps, fps));
                    using var image = new Mat();
                    if (!capture.Read(image) || image.Empty())
                    {
                        continue;
                    }

                    using var frame = new Frame(image.Clone(), start + offset, index);
                    FrameResult result = pipeline.Process(frame);
                    Rect band = result.Band;
                    List<Rect> privateAreas = [.. site.IgnoreZones.Select(z => new Rect(
                        (int)(z.X * image.Width), (int)(z.Y * image.Height), (int)Math.Ceiling(z.Width * image.Width), (int)Math.Ceiling(z.Height * image.Height)))];
                    using Mat crop = new Mat(image, band).Clone();
                    Anonymizer.Apply(crop, result.Detections.Select(d => (d.Box, d.Class)), (detector as IPersonDetections)?.LastPersons ?? [], band.Location, privateAreas);
                    Cv2.ImWrite(Path.Combine(folder, name + ".jpg"), crop, new ImageEncodingParam(ImwriteFlags.JpegQuality, 90));
                    images.Add(new
                    {
                        File = name + ".jpg",
                        Time = start + offset,
                        band.Width,
                        band.Height,
                        Boxes = result.Detections.Select(d => new
                        {
                            TrackId = 0,
                            Class = d.Class.ToString(),
                            d.Confidence,
                            X = d.Box.Left - band.X,
                            Y = d.Box.Top - band.Y,
                            d.Box.Width,
                            d.Box.Height,
                        }),
                    });
                }

                var metadata = new
                {
                    Note = "Anonymised training sample from a hand-counted recording (ADR-0026) - keep in the private repository only.",
                    Time = at,
                    Direction = passage.Motion.ToString(),
                    Class = (passage.Class ?? vehicleClass).ToString(),
                    ClassConfidence = (float?)null,
                    Trailers = (int?)null,
                    SpeedKmh = (float?)null,
                    Lighting = lighting.Classify(at).ToString(),
                    Reasons = new[] { (reason ?? $"hand count {vehicleClass}") + (passage.Note is { Length: > 0 } note ? $" ({note})" : "") },
                    Source = Path.GetFileName(video),
                    Images = images,
                };
                File.WriteAllText(Path.Combine(folder, "sample.json"), JsonSerializer.Serialize(metadata, JsonOptions));
                videoWritten++;
            }

            Console.WriteLine($"{Path.GetFileName(video)}: {passages.Count} × {(passagesFor is null ? vehicleClass.ToString() : "passage")}, {videoWritten} new samples");
            written += videoWritten;
        }

        Console.WriteLine($"{written - recutCount} samples written to {inbox}{(recutCount > 0 ? $", {recutCount} cut again" : "")}{(existing > 0 ? $", {existing} existed already" : "")}"
            + (reclassified > 0 ? $", {reclassified} of them reclassified (relabel them in Label Studio if already exported)" : ""));
        return 0;
    }

    /// <summary>Folder of a sample already cut from the same recording at the same time, whatever its class.</summary>
    private static string? FindExisting(string day, string pattern, string source, DateTimeOffset at) =>
        !Directory.Exists(day) ? null : Directory.GetDirectories(day, pattern).FirstOrDefault(folder =>
        {
            string file = Path.Combine(folder, "sample.json");
            if (!File.Exists(file) || JsonNode.Parse(File.ReadAllText(file)) is not JsonObject sample)
            {
                return false;
            }

            return sample["Source"]?.GetValue<string>() == source
                && sample["Time"]?.GetValue<DateTimeOffset>() is DateTimeOffset time
                && Math.Abs((time - at).TotalMilliseconds) < 1;
        });

    /// <summary>Gives an existing sample the class of the hand count, if it changed; true if it did.</summary>
    private static bool Reclassify(string folder, VehicleClass vehicleClass, string? note)
    {
        string file = Path.Combine(folder, "sample.json");
        var sample = (JsonObject)JsonNode.Parse(File.ReadAllText(file))!;
        if (sample["Class"]?.GetValue<string>() == vehicleClass.ToString())
        {
            return false;
        }

        sample["Class"] = vehicleClass.ToString();
        sample["Reasons"] = new JsonArray($"hand count {vehicleClass}" + (note is { Length: > 0 } ? $" ({note})" : ""));
        File.WriteAllText(file, sample.ToJsonString(JsonOptions));
        return true;
    }

    private static int FrameAt(TimeSpan offset, IReadOnlyList<DateTimeOffset>? timestamps, double fps)
    {
        if (timestamps is not { Count: > 1 })
        {
            return Math.Max(0, (int)(offset.TotalSeconds * fps));
        }

        DateTimeOffset target = timestamps[0] + offset;
        int low = 0, high = timestamps.Count - 1;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (timestamps[middle] < target)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
