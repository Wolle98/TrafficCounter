using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Detection.Onnx.Tests;

public class YoloPostprocessingTests
{
    [Fact]
    public void Letterbox_scales_the_wide_band_into_the_wide_input()
    {
        // 1920 x 510 band into 1280 x 384: width limits the scale (2/3), vertical padding (384 - 340) / 2 = 22.
        Letterbox letterbox = Letterbox.For(1920, 510, 1280, 384);

        Assert.Equal(2f / 3f, letterbox.Scale, 5);
        Assert.Equal(0f, letterbox.PadX, 3);
        Assert.Equal(22f, letterbox.PadY, 3);
    }

    [Fact]
    public void Letterbox_maps_boxes_back_to_frame_coordinates()
    {
        Letterbox letterbox = Letterbox.For(1920, 510, 1280, 384);
        var inModel = new BoundingBox(100, 22 + 20, 160, 60); // (150, 30, 240, 90) in the band

        BoundingBox inFrame = letterbox.ToSource(inModel, offsetX: 0, offsetY: 360);

        Assert.Equal(150, inFrame.Left, 2);
        Assert.Equal(390, inFrame.Top, 2);
        Assert.Equal(240, inFrame.Width, 2);
        Assert.Equal(90, inFrame.Height, 2);
    }

    [Fact]
    public void Raw_output_is_decoded_with_best_class_per_anchor()
    {
        // 4 anchors, 3 classes → layout [cx, cy, w, h, s0, s1, s2] x anchors.
        const int anchors = 4;
        float[,] rows =
        {
            { 100, 200, 300, 400 },    // cx
            { 50, 60, 70, 80 },        // cy
            { 20, 20, 20, 20 },        // w
            { 10, 10, 10, 10 },        // h
            { 0.9f, 0.05f, 0.2f, 0.3f }, // class 0 (e.g. person, not wanted)
            { 0.3f, 0.05f, 0.7f, 0.2f }, // class 1
            { 0.2f, 0.05f, 0.1f, 0.6f }, // class 2
        };
        float[] output = Flatten(rows);

        List<RawDetection> detections = YoloPostprocessing.DecodeRaw(output, channels: 7, anchors, minScore: 0.25f, isWanted: c => c != 0);

        Assert.Equal(3, detections.Count); // anchor 1 is below the threshold everywhere
        Assert.Equal((1, 0.3f), (detections[0].ClassIndex, detections[0].Score)); // best wanted class, not person
        Assert.Equal(new BoundingBox(90, 45, 20, 10), detections[0].Box);
        Assert.Equal(1, detections[1].ClassIndex);
        Assert.Equal(2, detections[2].ClassIndex);
    }

    [Fact]
    public void End_to_end_output_is_decoded()
    {
        float[] output =
        [
            10, 20, 110, 70, 0.8f, 2,
            0, 0, 5, 5, 0.05f, 2,  // too weak
            30, 40, 60, 90, 0.9f, 0, // unwanted class
        ];

        List<RawDetection> detections = YoloPostprocessing.DecodeEndToEnd(output, count: 3, minScore: 0.25f, isWanted: c => c == 2);

        RawDetection single = Assert.Single(detections);
        Assert.Equal(BoundingBox.FromCorners(10, 20, 110, 70), single.Box);
    }

    [Fact]
    public void Nms_merges_overlapping_boxes_across_classes()
    {
        // A van detected as "car" and "truck" at the same time must stay one object.
        List<RawDetection> detections =
        [
            new(new BoundingBox(100, 100, 200, 100), ClassIndex: 2, Score: 0.7f),
            new(new BoundingBox(105, 102, 200, 100), ClassIndex: 7, Score: 0.8f),
            new(new BoundingBox(600, 100, 200, 100), ClassIndex: 2, Score: 0.6f),
        ];

        List<RawDetection> kept = YoloPostprocessing.NonMaximumSuppression(detections, iouThreshold: 0.5f);

        Assert.Equal(2, kept.Count);
        Assert.Equal(7, kept[0].ClassIndex);
        Assert.Equal(600, kept[1].Box.Left);
    }

    [Fact]
    public void Class_names_are_parsed_from_ultralytics_metadata()
    {
        Dictionary<int, string> names = YoloPostprocessing.ParseClassNames("{0: 'person', 1: 'bicycle', 2: 'car', 79: 'toothbrush'}");

        Assert.Equal(4, names.Count);
        Assert.Equal("car", names[2]);
        Assert.Equal("toothbrush", names[79]);
    }

    private static float[] Flatten(float[,] rows)
    {
        int channels = rows.GetLength(0);
        int anchors = rows.GetLength(1);
        float[] flat = new float[channels * anchors];
        for (int c = 0; c < channels; c++)
        {
            for (int a = 0; a < anchors; a++)
            {
                flat[(c * anchors) + a] = rows[c, a];
            }
        }

        return flat;
    }
}
