using OpenCvSharp;

namespace TrafficCounter.Vision;

/// <summary>
/// Counts the bright, round, out-of-focus discs that raindrops on a window pane form at night, when street lamps and
/// headlights light them up (ADR-0024). On a dry night the same count stays low: only lamps, lit windows and road
/// markings respond. Works on the whole frame, scaled to 640 px width, with a difference of Gaussians at the scale of
/// the discs.
/// </summary>
public static class RaindropDetector
{
    private const int AnalysisWidth = 640;

    private static SimpleBlobDetector.Params BlobParams(float minArea, float maxArea) => new()
    {
        FilterByColor = true,
        BlobColor = 255,
        MinThreshold = 8,
        MaxThreshold = 200,
        ThresholdStep = 8,
        FilterByArea = true,
        MinArea = minArea,
        MaxArea = maxArea,
        FilterByCircularity = true,
        MinCircularity = 0.7f,
        FilterByConvexity = true,
        MinConvexity = 0.85f,
        FilterByInertia = true,
        MinInertiaRatio = 0.5f,
    };

    /// <summary>Number of drop-like discs in the frame.</summary>
    /// <param name="fineSigma">Scale of the discs (Gaussian sigma at 640 px width); the defaults fit the f/1.0 IP camera,
    /// whose discs are about 10-16 px wide at that width. A camera with a smaller aperture has smaller discs.</param>
    /// <param name="coarseSigma">Scale of the background subtracted from them.</param>
    /// <param name="minArea">Smallest disc area in pixels at 640 px width.</param>
    /// <param name="maxArea">Largest disc area in pixels at 640 px width.</param>
    public static int CountDiscs(Mat frame, double fineSigma = 3.5, double coarseSigma = 10, float minArea = 20, float maxArea = 400)
    {
        using var gray = new Mat();
        if (frame.Channels() == 1)
        {
            frame.CopyTo(gray);
        }
        else
        {
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        }

        using var small = new Mat();
        double scale = (double)AnalysisWidth / gray.Width;
        Cv2.Resize(gray, small, new Size(AnalysisWidth, (int)Math.Round(gray.Height * scale)), 0, 0, InterpolationFlags.Area);
        using var smallFloat = new Mat();
        small.ConvertTo(smallFloat, MatType.CV_32F);

        using var fine = new Mat();
        using var coarse = new Mat();
        Cv2.GaussianBlur(smallFloat, fine, new Size(0, 0), fineSigma);
        Cv2.GaussianBlur(smallFloat, coarse, new Size(0, 0), coarseSigma);
        using var dog = new Mat();
        Cv2.Subtract(fine, coarse, dog);
        using var response = new Mat();
        dog.ConvertTo(response, MatType.CV_8U, 8); // saturates: negative responses become 0

        using SimpleBlobDetector detector = SimpleBlobDetector.Create(BlobParams(minArea, maxArea));
        return detector.Detect(response).Length;
    }
}
