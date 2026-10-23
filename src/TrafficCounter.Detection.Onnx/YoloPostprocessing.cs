using System.Text.RegularExpressions;
using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Detection.Onnx;

/// <summary>A raw model detection in model input coordinates.</summary>
internal readonly record struct RawDetection(BoundingBox Box, int ClassIndex, float Score);

/// <summary>Letterbox transform between frame region and model input.</summary>
internal readonly record struct Letterbox(float Scale, float PadX, float PadY, int InputWidth, int InputHeight)
{
    public static Letterbox For(int sourceWidth, int sourceHeight, int inputWidth, int inputHeight)
    {
        float scale = Math.Min((float)inputWidth / sourceWidth, (float)inputHeight / sourceHeight);
        float padX = (inputWidth - (sourceWidth * scale)) / 2;
        float padY = (inputHeight - (sourceHeight * scale)) / 2;
        return new Letterbox(scale, padX, padY, inputWidth, inputHeight);
    }

    /// <summary>Maps a box from model input back to the source region and adds the region offset.</summary>
    public BoundingBox ToSource(BoundingBox box, float offsetX, float offsetY) => new(
        ((box.Left - PadX) / Scale) + offsetX,
        ((box.Top - PadY) / Scale) + offsetY,
        box.Width / Scale,
        box.Height / Scale);
}

/// <summary>Decodes Ultralytics YOLO outputs and applies non-maximum suppression.</summary>
internal static partial class YoloPostprocessing
{
    /// <summary>
    /// Raw head output [1, 4 + classes, anchors]: centre x/y, width, height, then one score per class
    /// (YOLOv8/YOLO11 export without NMS).
    /// </summary>
    public static List<RawDetection> DecodeRaw(ReadOnlySpan<float> output, int channels, int anchors, float minScore, Func<int, bool> isWanted)
    {
        int classes = channels - 4;
        List<RawDetection> detections = [];
        for (int a = 0; a < anchors; a++)
        {
            int bestClass = -1;
            float bestScore = minScore;
            for (int c = 0; c < classes; c++)
            {
                float score = output[((4 + c) * anchors) + a];
                if (score >= bestScore && isWanted(c))
                {
                    bestScore = score;
                    bestClass = c;
                }
            }

            if (bestClass < 0)
            {
                continue;
            }

            float cx = output[a];
            float cy = output[anchors + a];
            float w = output[(2 * anchors) + a];
            float h = output[(3 * anchors) + a];
            detections.Add(new RawDetection(new BoundingBox(cx - (w / 2), cy - (h / 2), w, h), bestClass, bestScore));
        }

        return detections;
    }

    /// <summary>End-to-end output [1, detections, 6]: x1, y1, x2, y2, score, class (NMS-free models such as YOLO26).</summary>
    public static List<RawDetection> DecodeEndToEnd(ReadOnlySpan<float> output, int count, float minScore, Func<int, bool> isWanted)
    {
        List<RawDetection> detections = [];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<float> row = output.Slice(i * 6, 6);
            int classIndex = (int)row[5];
            if (row[4] >= minScore && isWanted(classIndex))
            {
                detections.Add(new RawDetection(BoundingBox.FromCorners(row[0], row[1], row[2], row[3]), classIndex, row[4]));
            }
        }

        return detections;
    }

    /// <summary>Greedy non-maximum suppression across all classes.</summary>
    public static List<RawDetection> NonMaximumSuppression(List<RawDetection> detections, float iouThreshold)
    {
        List<RawDetection> kept = [];
        foreach (RawDetection candidate in detections.OrderByDescending(d => d.Score))
        {
            if (kept.All(k => k.Box.IntersectionOverUnion(candidate.Box) <= iouThreshold))
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }

    /// <summary>Parses the "names" metadata of Ultralytics exports: "{0: 'person', 1: 'bicycle', ...}".</summary>
    public static Dictionary<int, string> ParseClassNames(string metadata)
    {
        Dictionary<int, string> names = [];
        foreach (Match match in ClassNamePattern().Matches(metadata))
        {
            names[int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)] = match.Groups[2].Value;
        }

        return names;
    }

    [GeneratedRegex(@"(\d+)\s*:\s*['""]([^'""]+)['""]")]
    private static partial Regex ClassNamePattern();
}
