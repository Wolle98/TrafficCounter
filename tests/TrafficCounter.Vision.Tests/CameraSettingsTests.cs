using OpenCvSharp;

namespace TrafficCounter.Vision.Tests;

public class CameraSettingsTests
{
    [Theory]
    [InlineData(-6, -7, true, -7, ControlSupport.Adjustable)]    // exactly as requested
    [InlineData(0, 10, true, 8, ControlSupport.Adjustable)]      // clamped/rounded by the camera
    [InlineData(1, 0, true, 0.25, ControlSupport.Adjustable)]    // V4L2-style auto exposure value
    [InlineData(50, 60, true, 50, ControlSupport.Ignored)]       // accepted but unchanged
    [InlineData(50, 60, false, 50, ControlSupport.NotSupported)]
    [InlineData(-1, 10, true, -1, ControlSupport.Unverified)]    // write-only control
    [InlineData(-1, 10, false, -1, ControlSupport.NotSupported)]
    public void Control_support_is_classified(double before, double target, bool accepted, double after, ControlSupport expected) =>
        Assert.Equal(expected, ControlSupportClassifier.Classify(before, target, accepted, after));

    [Theory]
    [InlineData(1, true)]      // generic / DirectShow "auto" flag
    [InlineData(0.75, true)]   // V4L2 style auto exposure
    [InlineData(0, false)]
    [InlineData(0.25, false)]  // V4L2 style manual
    [InlineData(2, false)]     // DirectShow "manual" flag, e.g. autofocus after switching it off
    [InlineData(-1, false)]    // not supported
    public void Switch_values_are_interpreted(double value, bool expected) =>
        Assert.Equal(expected, CameraSettings.IsOn(value));

    [Fact]
    public void FourCC_is_decoded() =>
        Assert.Equal("MJPG", CameraSettings.DecodeFourCC(VideoWriter.FourCC('M', 'J', 'P', 'G')));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Missing_FourCC_decodes_to_null(double value) =>
        Assert.Null(CameraSettings.DecodeFourCC(value));

    [Fact]
    public void Controls_round_trip_through_options()
    {
        var options = new UsbCameraOptions();
        foreach (CameraControl control in CameraControl.All)
        {
            control.Write(options, control.IsSwitch ? 1 : 42);
        }

        foreach (CameraControl control in CameraControl.All)
        {
            Assert.Equal(control.IsSwitch ? 1 : 42, control.Read(options));
        }
    }

    [Fact]
    public void Automatic_modes_are_applied_before_their_manual_values()
    {
        List<VideoCaptureProperties> order = [.. CameraControl.All.Select(c => c.Property)];
        Assert.True(order.IndexOf(VideoCaptureProperties.AutoExposure) < order.IndexOf(VideoCaptureProperties.Exposure));
        Assert.True(order.IndexOf(VideoCaptureProperties.AutoFocus) < order.IndexOf(VideoCaptureProperties.Focus));
        Assert.True(order.IndexOf(VideoCaptureProperties.AutoWB) < order.IndexOf(VideoCaptureProperties.WBTemperature));
    }
}
