using OpenCvSharp;

namespace TrafficCounter.Vision;

public sealed class CameraUnavailableException(string message) : Exception(message);

/// <summary>Opens USB cameras and applies or reads back <see cref="UsbCameraOptions"/>.</summary>
public static class CameraSettings
{
    /// <summary>Value OpenCV returns for properties the backend does not support.</summary>
    public const double NotSupported = -1;

    public static VideoCaptureAPIs ToApi(CaptureBackend backend) => backend switch
    {
        CaptureBackend.DShow => VideoCaptureAPIs.DSHOW,
        CaptureBackend.Msmf => VideoCaptureAPIs.MSMF,
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null),
    };

    public static VideoCapture Open(UsbCameraOptions options)
    {
        int index = ResolveIndex(options);
        var capture = new VideoCapture(index, ToApi(options.Backend));
        if (!capture.IsOpened())
        {
            capture.Dispose();
            throw new CameraUnavailableException($"Camera {index} ({options.Backend}) could not be opened.");
        }

        ApplyFormat(capture, options.Width, options.Height, options.Fps, options.FourCC);
        ApplyControls(capture, options);
        return capture;
    }

    /// <summary>
    /// Device index to open: the device named <see cref="UsbCameraOptions.DeviceName"/> if set (DirectShow only),
    /// otherwise <see cref="UsbCameraOptions.DeviceIndex"/>.
    /// </summary>
    public static int ResolveIndex(UsbCameraOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DeviceName))
        {
            return options.DeviceIndex;
        }

        if (!OperatingSystem.IsWindows() || options.Backend != CaptureBackend.DShow)
        {
            throw new CameraUnavailableException("DeviceName needs the DirectShow backend on Windows.");
        }

        return DirectShowDevices.Find(options.DeviceName)
            ?? throw new CameraUnavailableException(
                $"No camera named '{options.DeviceName}' (found: {string.Join(", ", DirectShowDevices.List())}).");
    }

    public static void ApplyFormat(VideoCapture capture, int width, int height, double fps, string? fourCC)
    {
        // Order verified with a real UVC webcam (DirectShow): size, then frame rate, then pixel format.
        // Setting the frame rate after MJPG silently switches back to uncompressed YUY2 (4 fps at 1080p).
        capture.Set(VideoCaptureProperties.FrameWidth, width);
        capture.Set(VideoCaptureProperties.FrameHeight, height);
        capture.Set(VideoCaptureProperties.Fps, fps);
        if (fourCC is { Length: 4 })
        {
            // The char overload: VideoWriter.FourCC(string) produced a code that broke the DirectShow device.
            capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC(fourCC[0], fourCC[1], fourCC[2], fourCC[3]));
        }

        // Keep the driver queue short so frames are fresh; not every backend supports it.
        capture.Set(VideoCaptureProperties.BufferSize, 1);
    }

    public static void ApplyControls(VideoCapture capture, UsbCameraOptions options)
    {
        foreach (CameraControl control in CameraControl.All)
        {
            if (control.Read(options) is double value)
            {
                SetControl(capture, control, value);
            }
        }
    }

    public static bool SetControl(VideoCapture capture, CameraControl control, double value) =>
        control.Property == VideoCaptureProperties.AutoExposure
            ? SetAutoExposure(capture, value > 0.5)
            : capture.Set(control.Property, value);

    /// <summary>Reads the effective settings. Controls the backend does not report stay null.</summary>
    public static UsbCameraOptions ReadBack(VideoCapture capture, UsbCameraOptions requested)
    {
        UsbCameraOptions actual = new()
        {
            DeviceIndex = requested.DeviceIndex,
            Backend = requested.Backend,
            Width = (int)capture.Get(VideoCaptureProperties.FrameWidth),
            Height = (int)capture.Get(VideoCaptureProperties.FrameHeight),
            Fps = capture.Get(VideoCaptureProperties.Fps),
            FourCC = DecodeFourCC(capture.Get(VideoCaptureProperties.FourCC)),
        };

        foreach (CameraControl control in CameraControl.All)
        {
            double value = capture.Get(control.Property);
            if (value != NotSupported)
            {
                control.Write(actual, value);
            }
        }

        return actual;
    }

    public static string? DecodeFourCC(double value)
    {
        int code = (int)value;
        if (code <= 0)
        {
            return null;
        }

        char[] chars = [(char)(code & 0xFF), (char)((code >> 8) & 0xFF), (char)((code >> 16) & 0xFF), (char)((code >> 24) & 0xFF)];
        return chars.All(c => c is >= ' ' and <= '~') ? new string(chars) : null;
    }

    /// <summary>Interprets an on/off control value: 1 or V4L2-style 0.75 mean automatic; 0, 0.25 and DirectShow's
    /// "manual" flag 2 mean off.</summary>
    public static bool IsOn(double value) => value is > 0.5 and <= 1;

    /// <summary>
    /// Backends disagree on the value convention for auto exposure: some use 1/0, others the V4L2 style 0.75/0.25.
    /// DirectShow accepts the value but cannot report it back (-1); setting an exposure value switches it to manual.
    /// </summary>
    private static bool SetAutoExposure(VideoCapture capture, bool enabled)
    {
        double[] candidates = enabled ? [1.0, 0.75] : [0.25, 0.0];
        foreach (double candidate in candidates)
        {
            if (!capture.Set(VideoCaptureProperties.AutoExposure, candidate))
            {
                continue;
            }

            double readBack = capture.Get(VideoCaptureProperties.AutoExposure);
            if (readBack == NotSupported || Math.Abs(readBack - candidate) < 0.01)
            {
                return true;
            }
        }

        return false;
    }
}
