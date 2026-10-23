using OpenCvSharp;

namespace TrafficCounter.Vision;

/// <summary>A camera control that can be configured through <see cref="UsbCameraOptions"/> and probed.</summary>
/// <param name="Name">Name as used in <see cref="UsbCameraOptions"/>.</param>
/// <param name="IsSwitch">On/off control (stored as bool in the options).</param>
/// <param name="ProbeStep">Change applied by the probe to test whether the control has an effect.</param>
public sealed record CameraControl(
    string Name,
    VideoCaptureProperties Property,
    bool IsSwitch,
    double ProbeStep,
    Func<UsbCameraOptions, double?> Read,
    Action<UsbCameraOptions, double> Write)
{
    /// <summary>All supported controls. Order matters: automatic modes are applied before manual values.</summary>
    public static IReadOnlyList<CameraControl> All { get; } =
    [
        Switch(nameof(UsbCameraOptions.AutoFocus), VideoCaptureProperties.AutoFocus, o => o.AutoFocus, (o, v) => o.AutoFocus = v),
        Value(nameof(UsbCameraOptions.Focus), VideoCaptureProperties.Focus, 10, o => o.Focus, (o, v) => o.Focus = v),
        Switch(nameof(UsbCameraOptions.AutoExposure), VideoCaptureProperties.AutoExposure, o => o.AutoExposure, (o, v) => o.AutoExposure = v),
        Value(nameof(UsbCameraOptions.Exposure), VideoCaptureProperties.Exposure, -1, o => o.Exposure, (o, v) => o.Exposure = v),
        Value(nameof(UsbCameraOptions.Gain), VideoCaptureProperties.Gain, 10, o => o.Gain, (o, v) => o.Gain = v),
        Value(nameof(UsbCameraOptions.Brightness), VideoCaptureProperties.Brightness, 10, o => o.Brightness, (o, v) => o.Brightness = v),
        Value(nameof(UsbCameraOptions.Contrast), VideoCaptureProperties.Contrast, 10, o => o.Contrast, (o, v) => o.Contrast = v),
        Value(nameof(UsbCameraOptions.Saturation), VideoCaptureProperties.Saturation, 10, o => o.Saturation, (o, v) => o.Saturation = v),
        Value(nameof(UsbCameraOptions.Sharpness), VideoCaptureProperties.Sharpness, 10, o => o.Sharpness, (o, v) => o.Sharpness = v),
        Value(nameof(UsbCameraOptions.BacklightCompensation), VideoCaptureProperties.BackLight, 1, o => o.BacklightCompensation, (o, v) => o.BacklightCompensation = v),
        Switch(nameof(UsbCameraOptions.AutoWhiteBalance), VideoCaptureProperties.AutoWB, o => o.AutoWhiteBalance, (o, v) => o.AutoWhiteBalance = v),
        Value(nameof(UsbCameraOptions.WhiteBalanceTemperature), VideoCaptureProperties.WBTemperature, 500, o => o.WhiteBalanceTemperature, (o, v) => o.WhiteBalanceTemperature = v),
    ];

    public static CameraControl Get(VideoCaptureProperties property) => All.Single(c => c.Property == property);

    private static CameraControl Switch(
        string name, VideoCaptureProperties property, Func<UsbCameraOptions, bool?> read, Action<UsbCameraOptions, bool> write) =>
        new(name, property, IsSwitch: true, ProbeStep: 1,
            o => read(o) is bool b ? (b ? 1 : 0) : null,
            (o, v) => write(o, CameraSettings.IsOn(v)));

    private static CameraControl Value(
        string name, VideoCaptureProperties property, double step, Func<UsbCameraOptions, double?> read, Action<UsbCameraOptions, double> write) =>
        new(name, property, IsSwitch: false, step, read, write);
}
