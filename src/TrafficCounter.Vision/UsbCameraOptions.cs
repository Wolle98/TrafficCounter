namespace TrafficCounter.Vision;

public enum CaptureBackend
{
    /// <summary>DirectShow: broadest UVC control support, offers the driver settings dialog.</summary>
    DShow,

    /// <summary>Microsoft Media Foundation.</summary>
    Msmf,
}

/// <summary>
/// Settings for a USB (UVC) camera. Control values that are null are left as the camera has them.
/// Value ranges are device specific; use the camera probe to find working values (ADR-0017).
/// </summary>
public sealed class UsbCameraOptions
{
    public int DeviceIndex { get; set; }

    /// <summary>
    /// Part of the camera's name (e.g. "UGREEN"). If set, the DirectShow device with this name is used and
    /// <see cref="DeviceIndex"/> is ignored, so a webcam plugged in later cannot take its place.
    /// </summary>
    public string? DeviceName { get; set; }

    public CaptureBackend Backend { get; set; } = CaptureBackend.DShow;

    public int Width { get; set; } = 1920;

    public int Height { get; set; } = 1080;

    public double Fps { get; set; } = 30;

    /// <summary>Pixel format, e.g. "MJPG" or "YUY2". MJPG is needed for 1080p30 over USB 2.</summary>
    public string? FourCC { get; set; } = "MJPG";

    public bool? AutoFocus { get; set; }

    public double? Focus { get; set; }

    public bool? AutoExposure { get; set; }

    /// <summary>Exposure; for UVC cameras usually log2 of the exposure time in seconds (-7 ≈ 1/128 s).</summary>
    public double? Exposure { get; set; }

    public double? Gain { get; set; }

    public double? Brightness { get; set; }

    public double? Contrast { get; set; }

    public double? Saturation { get; set; }

    public double? Sharpness { get; set; }

    public double? BacklightCompensation { get; set; }

    public bool? AutoWhiteBalance { get; set; }

    public double? WhiteBalanceTemperature { get; set; }

    public UsbCameraOptions Clone() => (UsbCameraOptions)MemberwiseClone();
}
