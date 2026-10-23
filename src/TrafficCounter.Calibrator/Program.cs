using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using OpenCvSharp;
using TrafficCounter.Calibrator;
using TrafficCounter.Core;
using TrafficCounter.Pipeline;
using TrafficCounter.Vision;

Environment.SetEnvironmentVariable("OPENCV_LOG_LEVEL", "ERROR");

Dictionary<string, string> options = [];
for (int i = 0; i + 1 < args.Length; i += 2)
{
    options[args[i].TrimStart('-')] = args[i + 1];
}

if (args.Length == 0 || args[0] is "-h" or "--help" || (!options.ContainsKey("image") && !options.ContainsKey("video") && !options.ContainsKey("device") && !options.ContainsKey("rtsp")))
{
    Console.WriteLine(
        """
        TrafficCounter calibrator – set evaluation band, counting line, kerbs and static zones on a still image.

        Usage:
          TrafficCounter.Calibrator --image <photo.jpg> [--out <site.json>]
          TrafficCounter.Calibrator --video <recording.mp4> [--out <site.json>]
          TrafficCounter.Calibrator --device <n> [--out <site.json>]
          TrafficCounter.Calibrator --rtsp <url> [--user <name>] [--out <site.json>]
              (IP camera; password from the environment variable TrafficCounter__Source__Rtsp__Password)
          TrafficCounter.Calibrator --image <photo.jpg> --render <check.jpg>   (draw the saved configuration, no window)
          TrafficCounter.Calibrator --device <n> | --rtsp <url> --drift-only true [--out <site.json>]
              (no window: renews only the drift reference from the saved static zones, e.g. after a small
               camera movement; stop the service first, it holds the camera)

        With --device, the camera settings (focus, exposure, format) come from TrafficCounter:Source:Usb in the
        configuration, as in the service.

        Default output: %TRAFFICCOUNTER_CONFIG%, else %TRAFFICCOUNTER_PRIVATE%\config\site.json.
        Only the geometry is written; names and other settings in the file are kept. No image is saved.
        """);
    return 0;
}

try
{
    return Run(options);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static int Run(Dictionary<string, string> options)
{
    string output = options.GetValueOrDefault("out")
        ?? PrivateLocations.ConfigPath
        ?? throw new ArgumentException("Pass --out or set TRAFFICCOUNTER_CONFIG / TRAFFICCOUNTER_PRIVATE.");
    RecordingPolicy.EnsureNotInPublicRepository(output); // the site configuration is private (ADR-0002)

    using Mat image = LoadImage(options, output);
    if (image.Empty())
    {
        Console.Error.WriteLine("No image could be loaded.");
        return 1;
    }

    var session = new CalibrationSession(image, SiteConfigurationFile.Read(output), output, ModelAspect(output));
    if (options.TryGetValue("render", out string? renderPath))
    {
        RecordingPolicy.EnsureNotInPublicRepository(renderPath); // shows the site
        session.RenderTo(renderPath);
        Console.WriteLine($"Rendered to {renderPath}");
        return 0;
    }

    if (options.GetValueOrDefault("drift-only") is "true" or "yes")
    {
        Console.WriteLine(session.RenewDriftReference());
        return 0;
    }

    session.Run();
    return 0;
}

static Mat LoadImage(Dictionary<string, string> options, string configPath)
{
    if (options.TryGetValue("image", out string? imagePath))
    {
        return Cv2.ImRead(imagePath, ImreadModes.Color);
    }

    if (options.TryGetValue("rtsp", out string? rtspUrl))
    {
        RtspCameraOptions rtsp = RtspCameraOptions.ForTool(rtspUrl, options.GetValueOrDefault("user"));
        Console.WriteLine($"Opening {rtsp.DisplayUrl()} ...");
        return CaptureFrom(new RtspFrameSource(rtsp, NullLogger<RtspFrameSource>.Instance), "No frame from the IP camera. Check URL, user and password variable.");
    }

    if (!options.TryGetValue("video", out string? videoPath))
    {
        UsbCameraOptions camera = CameraOptionsFrom(configPath);
        camera.DeviceIndex = int.Parse(options["device"], System.Globalization.CultureInfo.InvariantCulture);
        return CaptureFromDevice(camera);
    }

    using var capture = new VideoCapture(videoPath, VideoCaptureAPIs.FFMPEG);
    var frame = new Mat();
    capture.Read(frame);
    return frame;
}

// Width/height of the detector input from the model file name in the configuration, e.g. "yolo11s-640x192.onnx" → 3.3.
static double? ModelAspect(string configPath)
{
    if (!File.Exists(configPath))
    {
        return null;
    }

    string? model = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configPath))?["TrafficCounter"]?["Detector"]?["ModelPath"]?.GetValue<string>();
    System.Text.RegularExpressions.Match size = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(model ?? ""), @"(\d+)x(\d+)");
    return size.Success ? double.Parse(size.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / double.Parse(size.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
}

// Camera settings as the service uses them (TrafficCounter:Source:Usb), e.g. the fixed focus; defaults otherwise.
static UsbCameraOptions CameraOptionsFrom(string configPath)
{
    if (!File.Exists(configPath))
    {
        return new UsbCameraOptions();
    }

    var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configPath));
    return json?["TrafficCounter"]?["Source"]?["Usb"]?.Deserialize<UsbCameraOptions>(
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new UsbCameraOptions();
}

// Same camera setup as the service (1080p MJPG, see UsbFrameSource); plain VideoCapture defaults would give a
// different resolution. Waits until auto exposure has settled – the first frames are often black.
static Mat CaptureFromDevice(UsbCameraOptions camera)
{
    Console.WriteLine($"Opening camera {camera.DeviceIndex} (close the live view and other camera apps first) ...");
    return CaptureFrom(
        new UsbFrameSource(camera, NullLogger<UsbFrameSource>.Instance), "No frame from the camera. Is it open in another program (live view, camera app, Teams)?");
}

// Takes frames for a few seconds and keeps the last one that is not black (auto exposure, stream start).
static Mat CaptureFrom(IFrameSource source, string failure)
{
    try
    {
        source.Start();
        Mat? latest = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(8))
        {
            if (!source.TryRead(TimeSpan.FromSeconds(1), out Frame? frame))
            {
                continue;
            }

            using (frame)
            {
                latest?.Dispose();
                latest = frame!.Image.Clone();
            }

            if (watch.Elapsed > TimeSpan.FromSeconds(3) && Cv2.Mean(latest).Val0 > 8)
            {
                break;
            }
        }

        if (latest is null)
        {
            Console.Error.WriteLine(failure);
            return new Mat();
        }

        Console.WriteLine($"Frame {latest.Width}x{latest.Height}, mean brightness {Cv2.Mean(latest).Val0:0}.");
        return latest;
    }
    finally
    {
        source.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
