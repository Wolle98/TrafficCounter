namespace TrafficCounter.Vision;

/// <summary>Outcome of trying to change a camera control.</summary>
public enum ControlSupport
{
    /// <summary>The backend rejected the change and cannot report a value.</summary>
    NotSupported,

    /// <summary>The value did not change (the camera ignores the control or it is locked by an automatic mode).</summary>
    Ignored,

    /// <summary>The backend accepted the change but cannot report the value – only the image tells.</summary>
    Unverified,

    /// <summary>The value changed as requested (possibly rounded or clamped by the camera).</summary>
    Adjustable,
}

public static class ControlSupportClassifier
{
    private const double Tolerance = 1e-3;

    public static ControlSupport Classify(double before, double target, bool accepted, double after)
    {
        if (Math.Abs(after - target) < Tolerance)
        {
            return ControlSupport.Adjustable;
        }

        if (after == CameraSettings.NotSupported)
        {
            return accepted ? ControlSupport.Unverified : ControlSupport.NotSupported;
        }

        if (Math.Abs(after - before) >= Tolerance)
        {
            return ControlSupport.Adjustable;
        }

        return accepted ? ControlSupport.Ignored : ControlSupport.NotSupported;
    }
}
