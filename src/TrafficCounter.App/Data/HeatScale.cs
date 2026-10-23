using System.Globalization;

namespace TrafficCounter.App.Data;

/// <summary>
/// Colour scale for the weekly heatmap: seven "heat" classes from pale yellow over orange and red to deep purple.
/// Several hues make neighbouring classes easy to tell apart, while the lightness falls monotonically, so the order
/// stays readable (also in greyscale). Discrete classes instead of a continuous ramp: a continuous ramp has to pass a
/// lightness band where neither dark nor white labels reach 4.5:1; the classes skip it. Each class carries the label
/// colour with the higher contrast.
/// </summary>
public static class HeatScale
{
    public const string DarkInk = "#1b1f2a";
    public const string LightInk = "#ffffff";

    private static readonly string[] Classes = ["#fff7d6", "#fee391", "#fec44f", "#fb9a29", "#ec6a1f", "#c4262e", "#7a1146"];

    public static int ClassCount => Classes.Length;

    /// <summary>The classes as a stepped CSS gradient, for the scale legend.</summary>
    public static string Gradient => "linear-gradient(90deg, " + string.Join(", ", Classes.Select((c, i) =>
        string.Create(CultureInfo.InvariantCulture, $"{c} {i * 100.0 / Classes.Length:0.#}% {(i + 1) * 100.0 / Classes.Length:0.#}%"))) + ")";

    /// <summary>Cell background and label colour for a share of the maximum (0..1).</summary>
    public static (string Background, string Text) For(double share)
    {
        int index = Math.Min(Classes.Length - 1, (int)(Math.Clamp(share, 0, 1) * Classes.Length));
        string background = Classes[index];
        (byte R, byte G, byte B) color = Parse(background);
        string text = Contrast(color, Parse(DarkInk)) >= Contrast(color, Parse(LightInk)) ? DarkInk : LightInk;
        return (background, text);
    }

    /// <summary>WCAG contrast ratio of two colours.</summary>
    public static double Contrast((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        double la = Luminance(a);
        double lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static (byte R, byte G, byte B) Parse(string hex) => (
        byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static double Luminance((byte R, byte G, byte B) c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }
}
