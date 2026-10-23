using System.Globalization;
using OpenCvSharp;

namespace TrafficCounter.Vision;

/// <summary>Rules for the explicit recording mode (ADR-0005).</summary>
public static class RecordingPolicy
{
    /// <summary>File that marks the root of the public repository.</summary>
    public const string PublicRepositoryMarker = "TrafficCounter.sln";

    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(60);

    /// <summary>Throws if <paramref name="path"/> lies inside the public repository.</summary>
    public static void EnsureNotInPublicRepository(string path)
    {
        for (DirectoryInfo? directory = new FileInfo(Path.GetFullPath(path)).Directory; directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, PublicRepositoryMarker)))
            {
                throw new ArgumentException($"Refusing to write into the public repository ({directory.FullName}) – this data shows the site and is private (ADR-0002, ADR-0005).");
            }
        }
    }

    /// <summary>Parses "0.32-0.78": top and bottom of the stored band as fractions of the image height.</summary>
    public static (double Top, double Bottom) ParseBand(string value)
    {
        string[] parts = value.Split('-');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double top)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double bottom)
            && top is >= 0 and < 1 && bottom > top && bottom <= 1)
        {
            return (top, bottom);
        }

        throw new ArgumentException("Band expects 'top-bottom' as fractions of the image height, e.g. 0.32-0.78.");
    }

    /// <summary>Full-width band in pixels; the height is made even because video encoders require it.</summary>
    public static Rect BandRect(Size size, double top, double bottom)
    {
        int y = (int)Math.Round(top * size.Height);
        int height = (int)Math.Round(bottom * size.Height) - y;
        return new Rect(0, y, size.Width, height - (height % 2));
    }
}
