using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Vision;

namespace TrafficCounter.Regression;

/// <summary>
/// Runs a (stronger) model over the images of the collected training samples and stores its boxes in each sample.json
/// as <c>TeacherBoxes</c>, next to the counting model's <c>Boxes</c>. The labelling tasks then propose these boxes,
/// so less has to be drawn by hand. Only images not processed with this model before are run.
/// </summary>
internal static class Prelabel
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static int Run(string inbox, IObjectDetector detector, string modelName)
    {
        int samples = 0, images = 0, boxes = 0;
        foreach (string file in Directory.EnumerateFiles(inbox, "sample.json", SearchOption.AllDirectories).Order())
        {
            var sample = (JsonObject)JsonNode.Parse(File.ReadAllText(file))!;
            if (sample["TeacherModel"]?.GetValue<string>() == modelName || sample["Images"] is not JsonArray list)
            {
                continue;
            }

            foreach (JsonObject image in list.OfType<JsonObject>())
            {
                string path = Path.Combine(Path.GetDirectoryName(file)!, image["File"]!.GetValue<string>());
                if (!File.Exists(path))
                {
                    continue;
                }

                using Mat mat = Cv2.ImRead(path);
                IReadOnlyList<Core.Detection> found = detector.Detect(mat, new Rect(0, 0, mat.Width, mat.Height));
                var array = new JsonArray();
                foreach (Core.Detection d in found)
                {
                    array.Add(new JsonObject
                    {
                        ["Class"] = d.Class.ToString(),
                        ["Confidence"] = d.Confidence,
                        ["X"] = d.Box.Left,
                        ["Y"] = d.Box.Top,
                        ["Width"] = d.Box.Width,
                        ["Height"] = d.Box.Height,
                    });
                }

                image["TeacherBoxes"] = array;
                images++;
                boxes += found.Count;
            }

            sample["TeacherModel"] = modelName;
            File.WriteAllText(file, sample.ToJsonString(JsonOptions));
            samples++;
            if (samples % 200 == 0)
            {
                Console.WriteLine($"  {samples} samples ...");
            }
        }

        Console.WriteLine($"{samples} samples ({images} images) labelled in advance with {modelName}: {boxes} boxes");
        return 0;
    }
}
