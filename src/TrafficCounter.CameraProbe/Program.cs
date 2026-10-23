using Microsoft.Extensions.Logging;
using TrafficCounter.CameraProbe;
using TrafficCounter.Vision;

// Media Foundation opens many webcams very slowly with hardware transforms enabled.
// Must be set before OpenCV initialises its video I/O.
Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0");

// OpenCV warns on every index it cannot open while scanning; the probe reports that itself.
Environment.SetEnvironmentVariable("OPENCV_LOG_LEVEL", "ERROR");

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

try
{
    var arguments = new Arguments(args[1..]);
    UsbCameraOptions options = arguments.ToCameraOptions();
    RtspCameraOptions? rtsp = arguments.ToRtspOptions();

    using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder
        .AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        })
        .SetMinimumLevel(LogLevel.Information));

    switch (args[0])
    {
        case "list":
            DeviceList.Run(namesOnly: arguments.Get("names-only") is "true" or "yes");
            return 0;
        case "probe" when rtsp is not null:
            return await ProbeCommand.RunRtspAsync(rtsp, loggerFactory.CreateLogger<RtspFrameSource>(), arguments.Get("json"));
        case "probe":
            return ProbeCommand.Run(options, arguments.Get("json"));
        case "live":
            return await LiveCommand.RunAsync(CreateSource(), options);
        case "record":
            return await RecordCommand.RunAsync(CreateSource(), rtsp?.Fps ?? options.Fps, rtsp, arguments);
        case "monitor":
            return await MonitorCommand.RunAsync(CreateSource(), rtsp?.DisplayUrl() ?? $"USB device {options.DeviceIndex}", arguments);
        case "compress":
            return CompressCommand.Run(arguments);
        default:
            Console.Error.WriteLine($"Unknown command '{args[0]}'.");
            PrintUsage();
            return 2;
    }

    // The same sources as the counting service: USB camera, or IP camera with --rtsp.
    IFrameSource CreateSource() => rtsp is not null
        ? new RtspFrameSource(rtsp, loggerFactory.CreateLogger<RtspFrameSource>())
        : new UsbFrameSource(options, loggerFactory.CreateLogger<UsbFrameSource>());
}
catch (UsageException ex)
{
    Console.Error.WriteLine(ex.Message);
    PrintUsage();
    return 2;
}
catch (ArgumentException ex)
{
    // E.g. a recording target refused by the privacy rules.
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unexpected error: {ex}");
    return 1;
}

static void PrintUsage() => Console.WriteLine(
    """
    TrafficCounter camera probe

    Usage:
      TrafficCounter.CameraProbe list [--names-only true]
      TrafficCounter.CameraProbe probe [camera options] [--json <file>]
      TrafficCounter.CameraProbe live  [camera options]
      TrafficCounter.CameraProbe record [camera options] [--minutes 15] [--out <file.mp4>] [--band 0.32-0.78] [--tags night,rain]
                                        [--compress auto|on|off] [H.264 options]
      TrafficCounter.CameraProbe compress --video <file.mp4|folder> [--keep-original true] [H.264 options]
      TrafficCounter.CameraProbe monitor [camera options] [--minutes 0] [--out <file.csv>]
                                        one CSV line per minute: frames received, dropped, reconnects, longest gap;
                                        run beside the counting service to tell camera and network problems apart

    Camera options:
      --device <n>          Device index (default 0)
      --device-name <text>  Part of the camera name instead of the index (DirectShow), e.g. UGREEN
      --backend dshow|msmf  Capture backend (default dshow)
      --width <px> --height <px> --fps <n> --fourcc MJPG|YUY2   (default 1920x1080, 30, MJPG)
      --exposure <v> --gain <v> --focus <v>                      Manual values (disable the automatic mode)

    IP camera (probe, live, record, monitor) instead of the camera options:
      --rtsp <url>          Stream URL without credentials, e.g. rtsp://192.168.1.64:554/Streaming/Channels/101
      --user <name>         User; the password is read from the environment variable
                            TrafficCounter__Source__Rtsp__Password (never pass it on the command line)
      --fps <n>             Nominal stream rate for recordings (default 25)

    Live view keys:
      q/Esc quit   a auto exposure on/off   e/E exposure -/+   g/G gain -/+
      f autofocus on/off   [/] focus -/+   s driver settings dialog (DirectShow)
      h horizontal guides   p print current settings as JSON

    Recording (reference videos only, ADR-0005):
      Writes <file>.mp4, <file>.mp4.frames.csv (frame timestamps) and <file>.mp4.json (metadata).
      Default target: $TRAFFICCOUNTER_PRIVATE\recordings\<timestamp>.mp4. Targets inside the public
      repository are refused. Maximum 60 minutes.
      OpenCV writes MPEG-4 (several GB per hour). If ffmpeg is found (PATH, WinGet or --ffmpeg), the video is
      converted to H.264 afterwards under the same name; the original is replaced only if the frame count matches.

    H.264 options:
      --ffmpeg <path>       ffmpeg.exe (default: PATH, then the WinGet links folder)
      --encoder libx264|h264_qsv|h264_nvenc   CPU, Intel Quick Sync or NVIDIA (default libx264)
      --quality <n>         CRF/CQ value, lower = better and larger (default 26)

    Apart from 'record', no images are saved.
    """);
