using OpenCvSharp;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.Pipeline;

public enum EnhancementMode
{
    None,

    /// <summary>Local contrast enhancement on the lightness channel (CLAHE), ADR-0017 point 6.</summary>
    Clahe,

    /// <summary>Brightens dark areas with a gamma curve, roughly like a higher camera gain.</summary>
    Gamma,

    /// <summary>Grey levels instead of colour: removes the colour noise of night images (all three channels equal).</summary>
    Grayscale,

    /// <summary>
    /// Experiment (regression only): subtracts the static bright spots of a slow background – light discs of raindrops
    /// on the pane in front of a lamp, but also lit road markings. Moving vehicles stay, except where they overlap
    /// such a spot.
    /// </summary>
    StaticHighlights,
}

/// <summary>Optional image enhancement of the evaluation band before detection, e.g. for dark or rainy nights.</summary>
public sealed class EnhancementOptions
{
    public EnhancementMode Mode { get; set; } = EnhancementMode.None;

    /// <summary>Applies at night only (default) or also at dusk or always.</summary>
    public LightingCondition From { get; set; } = LightingCondition.Night;

    /// <summary>CLAHE contrast limit; higher amplifies more (and more noise).</summary>
    public double ClipLimit { get; set; } = 3.0;

    /// <summary>CLAHE tiles across the band width.</summary>
    public int Tiles { get; set; } = 8;

    /// <summary>Gamma below 1 brightens: 0.5 lifts a value of 25 to about 80.</summary>
    public double Gamma { get; set; } = 0.5;

    public bool AppliesTo(LightingCondition lighting) => Mode != EnhancementMode.None && lighting switch
    {
        LightingCondition.Night => true,
        LightingCondition.Dusk => From is LightingCondition.Dusk or LightingCondition.Day,
        _ => From == LightingCondition.Day,
    };
}

/// <summary>
/// Enhances the band of a frame into a reusable frame-sized buffer, so the detector keeps working in frame coordinates
/// and the original frame (recording, samples) stays untouched. Only the band of the result is valid.
/// </summary>
internal sealed class ImageEnhancer(EnhancementOptions options) : IDisposable
{
    private readonly Mat _copy = new();
    private CLAHE? _clahe;
    private Mat? _lut;
    private Mat? _background; // slow running average of the band, float

    public Mat Enhance(Mat frame, Rect band)
    {
        // Only the band is copied (the detector reads nothing else); outside it the copy keeps stale content.
        _copy.Create(frame.Size(), frame.Type());
        using Mat region = new(_copy, band);
        using (Mat source = new(frame, band))
        {
            source.CopyTo(region);
        }

        switch (options.Mode)
        {
            case EnhancementMode.Clahe:
                _clahe ??= Cv2.CreateCLAHE(options.ClipLimit, new Size(options.Tiles, Math.Max(1, options.Tiles * band.Height / Math.Max(1, band.Width))));
                using (Mat lab = new())
                {
                    Cv2.CvtColor(region, lab, ColorConversionCodes.BGR2Lab);
                    Mat[] channels = Cv2.Split(lab);
                    try
                    {
                        _clahe.Apply(channels[0], channels[0]);
                        Cv2.Merge(channels, lab);
                    }
                    finally
                    {
                        foreach (Mat channel in channels)
                        {
                            channel.Dispose();
                        }
                    }

                    Cv2.CvtColor(lab, region, ColorConversionCodes.Lab2BGR);
                }

                break;
            case EnhancementMode.Gamma:
                _lut ??= GammaTable(options.Gamma);
                Cv2.LUT(region, _lut, region);
                break;
            case EnhancementMode.StaticHighlights:
                SubtractStaticHighlights(region);
                break;
            case EnhancementMode.Grayscale:
                using (Mat gray = new())
                {
                    Cv2.CvtColor(region, gray, ColorConversionCodes.BGR2GRAY);
                    Cv2.CvtColor(gray, region, ColorConversionCodes.GRAY2BGR);
                }

                break;
        }

        return _copy;
    }

    /// <summary>
    /// Keeps a running average of the band (time constant of a few seconds at 15 fps) and subtracts its static bright
    /// spots: the average minus its large-scale blur, only where positive.
    /// </summary>
    private void SubtractStaticHighlights(Mat region)
    {
        using Mat current = new();
        region.ConvertTo(current, MatType.CV_32FC3);
        if (_background is null || _background.Size() != current.Size())
        {
            _background?.Dispose();
            _background = current.Clone();
        }
        else
        {
            Cv2.AccumulateWeighted(current, _background, 0.02, null!);
        }

        using Mat smooth = new();
        Cv2.GaussianBlur(_background, smooth, new Size(0, 0), 15);
        using Mat highlights = new();
        Cv2.Subtract(_background, smooth, highlights);
        Cv2.Max(highlights, Scalar.All(0), highlights);
        Cv2.Subtract(current, highlights, current);
        current.ConvertTo(region, MatType.CV_8UC3); // saturates to 0..255
    }

    private static Mat GammaTable(double gamma)
    {
        var table = new Mat(1, 256, MatType.CV_8UC1);
        for (int i = 0; i < 256; i++)
        {
            table.Set(0, i, (byte)Math.Clamp(Math.Round(255 * Math.Pow(i / 255.0, gamma)), 0, 255));
        }

        return table;
    }

    public void Dispose()
    {
        _copy.Dispose();
        _clahe?.Dispose();
        _lut?.Dispose();
        _background?.Dispose();
    }
}
