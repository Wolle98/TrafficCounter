using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using TrafficCounter.Vision;

namespace TrafficCounter.CameraProbe;

/// <summary>
/// Live view with measured frame rate, exposure, brightness and sharpness; camera controls can be changed by key
/// (USB cameras only; an IP camera is set up in its web interface). Uses the same sources as the counting service.
/// Nothing is saved (ADR-0005).
/// </summary>
internal static class LiveCommand
{
    private const string WindowName = "TrafficCounter camera probe";
    private const int DisplayWidth = 1280;
    private const int SettingsRefreshFrames = 15;

    public static async Task<int> RunAsync(IFrameSource source, UsbCameraOptions options)
    {
        await using (source)
        {
            return await RunAsync(source, source as UsbFrameSource, options);
        }
    }

    private static async Task<int> RunAsync(IFrameSource source, UsbFrameSource? usb, UsbCameraOptions options)
    {
        source.Start();

        Cv2.NamedWindow(WindowName, WindowFlags.AutoSize);
        var fps = new FrameRateMeter();
        UsbCameraOptions? settings = null;
        Task<UsbCameraOptions>? pendingSettings = null;
        bool showGuides = true;
        bool autoExposure = options.AutoExposure ?? options.Exposure is null;
        long frames = 0;

        while (true)
        {
            if (source.TryRead(TimeSpan.FromMilliseconds(500), out Frame? frame))
            {
                using (frame)
                {
                    fps.Add(frame!.Timestamp);
                    if (usb is not null && ++frames % SettingsRefreshFrames == 1 && pendingSettings is null)
                    {
                        pendingSettings = usb.InvokeAsync(capture => CameraSettings.ReadBack(capture, options));
                    }

                    ImageMetrics metrics = ImageMetrics.Measure(frame.Image);
                    TimeSpan age = DateTimeOffset.Now - frame.Timestamp;
                    using Mat display = Render(frame.Image, metrics, fps.FramesPerSecond, age, source.Statistics, WithAutoExposure(settings, autoExposure), showGuides, usb is not null);
                    Cv2.ImShow(WindowName, display);
                }
            }
            else
            {
                using Mat waiting = new(360, 640, MatType.CV_8UC3, Scalar.Black);
                Cv2.PutText(waiting, "Waiting for camera ...", new Point(20, 180), HersheyFonts.HersheySimplex, 0.8, Scalar.White, 2);
                Cv2.ImShow(WindowName, waiting);
            }

            if (pendingSettings is { IsCompleted: true })
            {
                if (pendingSettings.IsCompletedSuccessfully)
                {
                    settings = pendingSettings.Result;
                }

                pendingSettings = null;
            }

            int key = Cv2.WaitKey(1);
            if (key is 'q' or 27 || Cv2.GetWindowProperty(WindowName, WindowPropertyFlags.Visible) < 1)
            {
                break;
            }

            if (key != -1)
            {
                Console.WriteLine($"key '{(key is >= 32 and < 127 ? (char)key : '?')}' ({key})");
            }

            switch (key)
            {
                case 'h':
                    showGuides = !showGuides;
                    Console.WriteLine($"guides {(showGuides ? "on" : "off")}");
                    break;
                case 'p':
                    Console.WriteLine(settings is null
                        ? "Settings not available yet."
                        : JsonSerializer.Serialize(WithAutoExposure(settings, autoExposure), ProbeCommand.JsonOptions));
                    break;
                case 's' when usb is not null:
                    await usb.InvokeAsync(capture => capture.Set(VideoCaptureProperties.Settings, 1));
                    break;
                default:
                    if (usb is not null && ControlChange(key, settings, autoExposure) is ({ } control, double value))
                    {
                        if (control.Property == VideoCaptureProperties.AutoExposure)
                        {
                            autoExposure = value > 0.5;
                        }
                        else if (control.Property == VideoCaptureProperties.Exposure)
                        {
                            autoExposure = false; // an exposure value switches the camera to manual
                        }

                        pendingSettings = usb.InvokeAsync(capture =>
                        {
                            CameraSettings.SetControl(capture, control, value);
                            return CameraSettings.ReadBack(capture, options);
                        });
                    }

                    break;
            }
        }

        Cv2.DestroyAllWindows();
        FrameSourceStatistics stats = source.Statistics;
        Console.WriteLine($"Captured {stats.Captured} frames, dropped {stats.Dropped}, reconnects {stats.Reconnects}.");
        return 0;
    }

    /// <summary>Fills in the tracked auto exposure state where the backend cannot report it.</summary>
    private static UsbCameraOptions? WithAutoExposure(UsbCameraOptions? settings, bool autoExposure)
    {
        if (settings is null || settings.AutoExposure is not null)
        {
            return settings;
        }

        UsbCameraOptions copy = settings.Clone();
        copy.AutoExposure = autoExposure;
        return copy;
    }

    /// <param name="autoExposure">
    /// Auto exposure state as tracked by this view – DirectShow cannot report it, so the setting is only known from
    /// what was last sent (an exposure value switches the camera to manual).
    /// </param>
    private static (CameraControl Control, double Value)? ControlChange(int key, UsbCameraOptions? current, bool autoExposure)
    {
        if (current is null)
        {
            return null;
        }

        return key switch
        {
            'a' => (CameraControl.Get(VideoCaptureProperties.AutoExposure), (current.AutoExposure ?? autoExposure) ? 0 : 1),
            'e' => (CameraControl.Get(VideoCaptureProperties.Exposure), (current.Exposure ?? 0) - 1),
            'E' => (CameraControl.Get(VideoCaptureProperties.Exposure), (current.Exposure ?? 0) + 1),
            'g' => (CameraControl.Get(VideoCaptureProperties.Gain), Math.Max(0, (current.Gain ?? 0) - 10)),
            'G' => (CameraControl.Get(VideoCaptureProperties.Gain), (current.Gain ?? 0) + 10),
            'f' => (CameraControl.Get(VideoCaptureProperties.AutoFocus), current.AutoFocus == true ? 0 : 1),
            '[' => (CameraControl.Get(VideoCaptureProperties.Focus), (current.Focus ?? 0) - 5),
            ']' => (CameraControl.Get(VideoCaptureProperties.Focus), (current.Focus ?? 0) + 5),
            _ => null,
        };
    }

    private static Mat Render(
        Mat image, ImageMetrics metrics, double fps, TimeSpan age, FrameSourceStatistics stats, UsbCameraOptions? settings, bool showGuides, bool controls)
    {
        Mat display = new();
        if (image.Width > DisplayWidth)
        {
            Cv2.Resize(image, display, new Size(DisplayWidth, image.Height * DisplayWidth / image.Width), 0, 0, InterpolationFlags.Area);
        }
        else
        {
            image.CopyTo(display);
        }

        if (showGuides)
        {
            // Horizontal guides help to mount the camera level, so the kerbs run parallel to the image rows.
            for (int i = 1; i < 10; i++)
            {
                int y = display.Height * i / 10;
                Cv2.Line(display, new Point(0, y), new Point(display.Width, y), new Scalar(0, 255, 255), 1);
            }
        }

        string exposure = settings?.Exposure is double e ? $"{e.ToString("0.##", CultureInfo.InvariantCulture)} {ExposureFormat.Describe(e)}" : "n/a";
        string[] lines = controls
            ?
            [
                $"{image.Width}x{image.Height} {settings?.FourCC}  {fps:0.0} fps  age {age.TotalMilliseconds:0} ms  dropped {stats.Dropped}  reconnects {stats.Reconnects}",
                $"exposure {exposure} auto {OnOff(settings?.AutoExposure)}  gain {Value(settings?.Gain)}  focus {Value(settings?.Focus)} auto {OnOff(settings?.AutoFocus)}",
                $"brightness {metrics.Brightness:0}  sharpness {metrics.Sharpness:0}",
                "q quit  a auto-exp  e/E exposure  g/G gain  f autofocus  [/] focus  s driver dialog  h guides  p print",
            ]
            :
            [
                $"{image.Width}x{image.Height}  {fps:0.0} fps  age {age.TotalMilliseconds:0} ms  dropped {stats.Dropped}  reconnects {stats.Reconnects}",
                $"brightness {metrics.Brightness:0}  sharpness {metrics.Sharpness:0}  (camera settings: web interface)",
                "q quit  h guides",
            ];

        int top = 8;
        foreach (string line in lines)
        {
            Cv2.PutText(display, Overlay.Ascii(line), new Point(11, top + 21), HersheyFonts.HersheySimplex, 0.6, Scalar.Black, 3);
            Cv2.PutText(display, Overlay.Ascii(line), new Point(10, top + 20), HersheyFonts.HersheySimplex, 0.6, Scalar.White, 1);
            top += 26;
        }

        return display;
    }

    private static string OnOff(bool? value) => value switch
    {
        true => "on",
        false => "off",
        null => "?",
    };

    private static string Value(double? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "n/a";
}
