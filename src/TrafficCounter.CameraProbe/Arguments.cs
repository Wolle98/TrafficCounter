using System.Globalization;
using TrafficCounter.Vision;

namespace TrafficCounter.CameraProbe;

/// <summary>Wrong or missing command line arguments; the usage text is shown.</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>Minimal parser for "--name value" pairs.</summary>
internal sealed class Arguments
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public Arguments(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                throw new UsageException($"Unexpected argument '{args[i]}'.");
            }

            _values[args[i][2..]] = args[++i];
        }
    }

    public string? Get(string name) => _values.GetValueOrDefault(name);

    /// <summary>IP camera instead of USB: <c>--rtsp &lt;url&gt; [--user &lt;name&gt;] [--fps &lt;n&gt;]</c>, password from the environment.</summary>
    public RtspCameraOptions? ToRtspOptions() => Get("rtsp") is { } url
        ? RtspCameraOptions.ForTool(url, Get("user"), Get("fps") is { } fps ? ParseDouble(fps, "fps") : null)
        : null;

    public UsbCameraOptions ToCameraOptions()
    {
        UsbCameraOptions options = new();
        if (Get("device") is { } device)
        {
            options.DeviceIndex = ParseInt(device, "device");
        }

        options.DeviceName = Get("device-name");

        if (Get("backend") is { } backend)
        {
            options.Backend = Enum.TryParse(backend, ignoreCase: true, out CaptureBackend parsed)
                ? parsed
                : throw new UsageException($"Unknown backend '{backend}'.");
        }

        if (Get("width") is { } width)
        {
            options.Width = ParseInt(width, "width");
        }

        if (Get("height") is { } height)
        {
            options.Height = ParseInt(height, "height");
        }

        if (Get("fps") is { } fps)
        {
            options.Fps = ParseDouble(fps, "fps");
        }

        if (Get("fourcc") is { } fourCC)
        {
            options.FourCC = fourCC.Length == 4 ? fourCC.ToUpperInvariant() : throw new UsageException("--fourcc needs 4 characters.");
        }

        if (Get("exposure") is { } exposure)
        {
            options.AutoExposure = false;
            options.Exposure = ParseDouble(exposure, "exposure");
        }

        if (Get("gain") is { } gain)
        {
            options.Gain = ParseDouble(gain, "gain");
        }

        if (Get("focus") is { } focus)
        {
            options.AutoFocus = false;
            options.Focus = ParseDouble(focus, "focus");
        }

        return options;
    }

    private static int ParseInt(string value, string name) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : throw new UsageException($"--{name}: '{value}' is not a whole number.");

    private static double ParseDouble(string value, string name) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            ? result
            : throw new UsageException($"--{name}: '{value}' is not a number.");
}
