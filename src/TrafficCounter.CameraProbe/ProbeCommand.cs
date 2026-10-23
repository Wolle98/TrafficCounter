using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCvSharp;
using TrafficCounter.Vision;

namespace TrafficCounter.CameraProbe;

/// <summary>
/// Automatic capability test: supported formats and frame rates, which controls can be changed,
/// and whether manual exposure, gain and focus actually change the image (ADR-0017).
/// </summary>
internal static class ProbeCommand
{
    private static readonly (int Width, int Height)[] Resolutions = [(640, 480), (1280, 720), (1920, 1080), (2560, 1440), (3840, 2160)];
    private static readonly string[] PixelFormats = ["MJPG", "YUY2"];

    // log2 seconds: -5 ≈ 1/32 s, -7 ≈ 1/128 s, -8 ≈ 1/256 s.
    private const double LongExposure = -5;
    private const double TargetExposure = -7;
    private const double ShortExposure = -8;

    /// <summary>
    /// IP camera: an RTSP stream has no controls to test (they are set in the camera's web interface), so this only
    /// measures what arrives: resolution, real frame rate, gaps, reconnects and image metrics over 15 seconds.
    /// </summary>
    public static async Task<int> RunRtspAsync(RtspCameraOptions options, Microsoft.Extensions.Logging.ILogger<RtspFrameSource> logger, string? jsonPath)
    {
        Console.WriteLine($"Probing {options.DisplayUrl()} for 15 s ...");
        await using var source = new RtspFrameSource(options, logger);
        source.Start();
        if (!source.TryRead(TimeSpan.FromSeconds(20), out Frame? first))
        {
            Console.Error.WriteLine("No frame within 20 s. Check URL, user, password variable and that RTSP is enabled in the camera.");
            return 1;
        }

        int width = first!.Image.Width, height = first.Image.Height;
        DateTimeOffset start = first.Timestamp, last = start;
        first.Dispose();
        long frames = 1;
        double maxGap = 0, brightness = 0, sharpness = 0;
        int measured = 0;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (!source.TryRead(TimeSpan.FromSeconds(2), out Frame? frame))
            {
                continue;
            }

            using (frame)
            {
                maxGap = Math.Max(maxGap, (frame!.Timestamp - last).TotalMilliseconds);
                last = frame.Timestamp;
                frames++;
                if (frames % 10 == 0)
                {
                    ImageMetrics metrics = ImageMetrics.Measure(frame.Image);
                    brightness += metrics.Brightness;
                    sharpness += metrics.Sharpness;
                    measured++;
                }
            }
        }

        FrameSourceStatistics stats = source.Statistics;
        double seconds = Math.Max(0.001, (last - start).TotalSeconds);
        var report = new
        {
            Url = options.DisplayUrl(),
            Width = width,
            Height = height,
            MeasuredFps = Math.Round((frames - 1) / seconds, 1),
            NominalFps = options.Fps,
            MaxGapMs = Math.Round(maxGap),
            stats.Dropped,
            stats.Reconnects,
            Brightness = measured > 0 ? Math.Round(brightness / measured) : (double?)null,
            Sharpness = measured > 0 ? Math.Round(sharpness / measured) : (double?)null,
        };
        Console.WriteLine($"{width}x{height}, {report.MeasuredFps} fps measured (nominal {options.Fps}), longest gap {report.MaxGapMs} ms, "
            + $"reconnects {stats.Reconnects}, brightness {report.Brightness}, sharpness {report.Sharpness}");
        if (report.MaxGapMs > 3 * 1000 / Math.Max(1, report.MeasuredFps))
        {
            Console.WriteLine("Gaps of several frames: try the sub stream or a lower bitrate in the camera; check the network cable.");
        }

        if (jsonPath is not null)
        {
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, JsonOptions));
            Console.WriteLine($"Report written to {jsonPath}");
        }

        return 0;
    }

    public static int Run(UsbCameraOptions options, string? jsonPath)
    {
        Console.WriteLine($"Probing camera {options.DeviceIndex} with {options.Backend}. Point it at a well-lit, static scene; this takes about a minute.");
        using var capture = new VideoCapture(options.DeviceIndex, CameraSettings.ToApi(options.Backend));
        if (!capture.IsOpened())
        {
            Console.Error.WriteLine("Camera could not be opened. Run 'list' to see available devices.");
            return 1;
        }

        ProbeReport report = new() { Device = options.DeviceIndex, Backend = options.Backend.ToString() };

        Console.WriteLine();
        Console.WriteLine("Formats (requested → delivered, measured frame rate):");
        foreach (FormatResult format in ProbeFormats(capture))
        {
            report.Formats.Add(format);
            Console.WriteLine($"  {format.Requested,-16} → {format.Width}x{format.Height} {format.FourCC ?? "?",-4}  {format.MeasuredFps,5:0.0} fps");
        }

        CameraSettings.ApplyFormat(capture, options.Width, options.Height, options.Fps, options.FourCC);
        Skip(capture, 10);
        report.OperatingMode = new FormatResult(
            $"{options.Width}x{options.Height} {options.FourCC}",
            (int)capture.Get(VideoCaptureProperties.FrameWidth),
            (int)capture.Get(VideoCaptureProperties.FrameHeight),
            CameraSettings.DecodeFourCC(capture.Get(VideoCaptureProperties.FourCC)),
            MeasureFps(capture));

        Console.WriteLine();
        Console.WriteLine($"Controls in operating mode {report.OperatingMode.Width}x{report.OperatingMode.Height} {report.OperatingMode.FourCC}:");
        Console.WriteLine($"  {"Control",-24} {"Before",8} {"Target",8} {"After",8}  Result");
        foreach (ControlResult control in ProbeControls(capture))
        {
            report.Controls.Add(control);
            Console.WriteLine($"  {control.Name,-24} {F(control.Before),8} {F(control.Target),8} {F(control.After),8}  {control.Support}");
        }

        Console.WriteLine();
        Console.WriteLine("Effect on the image:");
        foreach (EffectResult effect in ProbeEffects(capture))
        {
            report.Effects.Add(effect);
            Console.WriteLine($"  {effect.Name,-24} {Verdict(effect.Effective),-8} {effect.Detail}");
        }

        report.Verdicts.AddRange(Evaluate(report, options));
        Console.WriteLine();
        Console.WriteLine("Requirements from ADR-0017 (night test decision):");
        foreach (RequirementResult requirement in report.Verdicts)
        {
            Console.WriteLine($"  [{Verdict(requirement.Passed),-7}] {requirement.Requirement} – {requirement.Detail}");
        }

        Console.WriteLine();
        Console.WriteLine("Still to check at night with 'live': image usable at exposure ≤ 1/100 s, frame rate ≥ 20 fps, focus stable.");

        if (jsonPath is not null)
        {
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, JsonOptions));
            Console.WriteLine($"Report written to {jsonPath}");
        }

        return 0;
    }

    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static List<FormatResult> ProbeFormats(VideoCapture capture)
    {
        List<FormatResult> results = [];
        foreach (string fourCC in PixelFormats)
        {
            foreach ((int width, int height) in Resolutions)
            {
                CameraSettings.ApplyFormat(capture, width, height, 30, fourCC);
                Skip(capture, 3);
                results.Add(new FormatResult(
                    $"{width}x{height} {fourCC}",
                    (int)capture.Get(VideoCaptureProperties.FrameWidth),
                    (int)capture.Get(VideoCaptureProperties.FrameHeight),
                    CameraSettings.DecodeFourCC(capture.Get(VideoCaptureProperties.FourCC)),
                    MeasureFps(capture)));
            }
        }

        return results;
    }

    private static List<ControlResult> ProbeControls(VideoCapture capture)
    {
        List<ControlResult> results = [];
        foreach (CameraControl control in CameraControl.All)
        {
            double before = capture.Get(control.Property);
            double target = control.IsSwitch
                ? (CameraSettings.IsOn(before) ? 0 : 1)
                : (before == CameraSettings.NotSupported ? control.ProbeStep : before + control.ProbeStep);

            bool accepted = CameraSettings.SetControl(capture, control, target);
            Skip(capture, 2);
            double after = capture.Get(control.Property);
            results.Add(new ControlResult(control.Name, before, target, accepted, after,
                ControlSupportClassifier.Classify(before, target, accepted, after)));

            if (before != CameraSettings.NotSupported)
            {
                CameraSettings.SetControl(capture, control, before);
            }
        }

        return results;
    }

    private static List<EffectResult> ProbeEffects(VideoCapture capture)
    {
        CameraControl autoExposure = CameraControl.Get(VideoCaptureProperties.AutoExposure);
        CameraControl exposure = CameraControl.Get(VideoCaptureProperties.Exposure);
        CameraControl gain = CameraControl.Get(VideoCaptureProperties.Gain);
        CameraControl autoFocus = CameraControl.Get(VideoCaptureProperties.AutoFocus);
        CameraControl focus = CameraControl.Get(VideoCaptureProperties.Focus);

        double autoExposureBefore = capture.Get(autoExposure.Property);
        double exposureBefore = capture.Get(exposure.Property);
        double gainBefore = capture.Get(gain.Property);
        double autoFocusBefore = capture.Get(autoFocus.Property);
        double focusBefore = capture.Get(focus.Property);
        List<EffectResult> results = [];

        // Exposure: 1/32 s vs. 1/256 s must darken the image clearly if manual exposure really works.
        CameraSettings.SetControl(capture, autoExposure, 0);
        CameraSettings.SetControl(capture, exposure, LongExposure);
        ImageMetrics longExposure = Measure(capture);
        CameraSettings.SetControl(capture, exposure, ShortExposure);
        ImageMetrics shortExposure = Measure(capture);
        results.Add(Compare(
            "Manual exposure", longExposure.Brightness, shortExposure.Brightness, expectIncrease: false,
            $"brightness {longExposure.Brightness:0} at ≈1/32 s → {shortExposure.Brightness:0} at ≈1/256 s"));

        bool targetAccepted = CameraSettings.SetControl(capture, exposure, TargetExposure);
        double targetReadBack = capture.Get(exposure.Property);
        results.Add(new EffectResult(
            "Exposure ≈ 1/128 s", targetAccepted && Math.Abs(targetReadBack - TargetExposure) < 0.5 ? true : null,
            $"set {TargetExposure}, camera reports {F(targetReadBack)}"));

        // Gain at fixed exposure.
        CameraSettings.SetControl(capture, gain, 0);
        ImageMetrics lowGain = Measure(capture);
        // Gain ranges differ a lot between cameras (the UGREEN webcam accepts only 0..10 in manual mode).
        double maxGain = new[] { 100.0, 64, 32, 16, 10, 5 }
            .FirstOrDefault(g => capture.Set(gain.Property, g) && Math.Abs(capture.Get(gain.Property) - g) < 0.5);
        ImageMetrics highGain = Measure(capture);
        results.Add(Compare(
            "Gain", lowGain.Brightness, highGain.Brightness, expectIncrease: true,
            $"brightness {lowGain.Brightness:0} at gain 0 → {highGain.Brightness:0} at gain {maxGain} (highest accepted)"));

        // Focus: a manual change must alter sharpness; otherwise the camera may still be focusing on its own.
        if (focusBefore != CameraSettings.NotSupported)
        {
            CameraSettings.SetControl(capture, autoFocus, 0);
            CameraSettings.SetControl(capture, focus, focusBefore);
            ImageMetrics focused = Measure(capture);
            CameraSettings.SetControl(capture, focus, focusBefore + 60);
            ImageMetrics defocused = Measure(capture);
            double ratio = focused.Sharpness > 0 ? defocused.Sharpness / focused.Sharpness : 1;
            bool? effective = focused.Brightness < 15 ? null : Math.Abs(1 - ratio) > 0.2; // no sharpness in a dark image
            results.Add(new EffectResult(
                "Manual focus", effective,
                $"sharpness {focused.Sharpness:0} at {F(focusBefore)} → {defocused.Sharpness:0} at {F(focusBefore + 60)}"));
        }
        else
        {
            results.Add(new EffectResult("Manual focus", null, "focus value not readable"));
        }

        Restore(capture, gain, gainBefore);
        Restore(capture, focus, focusBefore);
        Restore(capture, autoFocus, autoFocusBefore);
        Restore(capture, exposure, exposureBefore);
        Restore(capture, autoExposure, autoExposureBefore == CameraSettings.NotSupported ? 1 : autoExposureBefore);
        return results;
    }

    private static IEnumerable<RequirementResult> Evaluate(ProbeReport report, UsbCameraOptions options)
    {
        FormatResult mode = report.OperatingMode!;
        bool modeReached = mode.Width == options.Width && mode.Height == options.Height;
        yield return new RequirementResult(
            $"{options.Width}x{options.Height} at ≥ 20 fps",
            modeReached && mode.MeasuredFps >= 19.5, // measurement tolerance
            $"delivered {mode.Width}x{mode.Height} {mode.FourCC} at {mode.MeasuredFps:0.0} fps (daylight; check again at night)");

        ControlResult autoFocus = report.Controls.Single(c => c.Name == nameof(UsbCameraOptions.AutoFocus));
        EffectResult manualFocus = report.Effects.Single(e => e.Name == "Manual focus");
        yield return new RequirementResult(
            "Autofocus can be switched off",
            autoFocus.Support is ControlSupport.Adjustable || manualFocus.Effective == true ? true
                : autoFocus.Support is ControlSupport.Unverified ? null : false,
            $"AutoFocus control: {autoFocus.Support}; manual focus effect: {Verdict(manualFocus.Effective)}");

        EffectResult manualExposure = report.Effects.Single(e => e.Name == "Manual exposure");
        yield return new RequirementResult("Manual exposure changes the image", manualExposure.Effective, manualExposure.Detail);

        EffectResult target = report.Effects.Single(e => e.Name == "Exposure ≈ 1/128 s");
        yield return new RequirementResult(
            "Exposure can be limited to ≤ 1/100 s",
            manualExposure.Effective == true ? (target.Effective ?? true) : manualExposure.Effective,
            target.Detail);
    }

    private static EffectResult Compare(string name, double first, double second, bool expectIncrease, string detail)
    {
        // Too dark or saturated scenes cannot show an effect.
        if (Math.Max(first, second) < 15 || Math.Min(first, second) > 245)
        {
            return new EffectResult(name, null, detail + " – scene too dark or too bright, repeat in daylight");
        }

        double ratio = (second + 1) / (first + 1);
        bool effective = expectIncrease ? ratio > 1.3 : ratio < 0.7;
        return new EffectResult(name, effective, detail);
    }

    private static void Restore(VideoCapture capture, CameraControl control, double value)
    {
        if (value != CameraSettings.NotSupported)
        {
            CameraSettings.SetControl(capture, control, value);
        }
    }

    private static ImageMetrics Measure(VideoCapture capture)
    {
        Skip(capture, 10); // let the camera settle after a change
        double brightness = 0;
        double sharpness = 0;
        int count = 0;
        using Mat frame = new();
        for (int i = 0; i < 5; i++)
        {
            if (capture.Read(frame) && !frame.Empty())
            {
                ImageMetrics metrics = ImageMetrics.Measure(frame);
                brightness += metrics.Brightness;
                sharpness += metrics.Sharpness;
                count++;
            }
        }

        return count == 0 ? default : new ImageMetrics(brightness / count, sharpness / count);
    }

    private static double MeasureFps(VideoCapture capture, int maxFrames = 30, double maxSeconds = 2.5)
    {
        using Mat frame = new();
        if (!capture.Read(frame))
        {
            return 0;
        }

        var watch = Stopwatch.StartNew();
        int frames = 0;
        while (frames < maxFrames && watch.Elapsed.TotalSeconds < maxSeconds && capture.Read(frame) && !frame.Empty())
        {
            frames++;
        }

        return frames == 0 ? 0 : frames / watch.Elapsed.TotalSeconds;
    }

    private static void Skip(VideoCapture capture, int frames)
    {
        using Mat frame = new();
        for (int i = 0; i < frames; i++)
        {
            capture.Read(frame);
        }
    }

    private static string F(double value) =>
        value == CameraSettings.NotSupported ? "n/a" : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Verdict(bool? passed) => passed switch
    {
        true => "OK",
        false => "FAILED",
        null => "UNCLEAR",
    };
}

internal sealed class ProbeReport
{
    public int Device { get; init; }

    public required string Backend { get; init; }

    public List<FormatResult> Formats { get; } = [];

    public FormatResult? OperatingMode { get; set; }

    public List<ControlResult> Controls { get; } = [];

    public List<EffectResult> Effects { get; } = [];

    public List<RequirementResult> Verdicts { get; } = [];
}

internal sealed record FormatResult(string Requested, int Width, int Height, string? FourCC, double MeasuredFps);

internal sealed record ControlResult(string Name, double Before, double Target, bool Accepted, double After, ControlSupport Support);

internal sealed record EffectResult(string Name, bool? Effective, string Detail);

internal sealed record RequirementResult(string Requirement, bool? Passed, string Detail);
