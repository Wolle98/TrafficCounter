using System.Globalization;
using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Evaluation;

/// <summary>Direction of travel as seen in the image; independent of how the counting line is defined.</summary>
public enum ImageMotion
{
    LeftToRight,
    RightToLeft,
}

/// <summary>One manually counted passage in a reference video.</summary>
/// <param name="Offset">Time since the first frame of the video when the vehicle crossed the reference line.</param>
/// <param name="Class">Null if not annotated.</param>
/// <param name="SpeedKmh">
/// Test drive (ADR-0035): in the file the speedometer reading at the line; the regression replaces it with the true
/// speed from the speedometer calibration. Null for ordinary traffic.
/// </param>
/// <param name="SmallVan">
/// A small van (high-roof estate such as Opel Combo or VW Caddy, ADR-0055): class Van, but counted as a car it is
/// no error, since many of them are built as cars too. In the file the class is "SmallVan".
/// </param>
public sealed record GroundTruthEntry(
    TimeSpan Offset, ImageMotion Motion, VehicleClass? Class = null, string? Note = null, float? SpeedKmh = null, bool SmallVan = false);

/// <summary>
/// Ground truth file "&lt;video&gt;.gt.csv": <c>offset_s;motion;class;note;speed_kmh</c>, one line per passage; the
/// speed column is optional. Test drives with known speed alone go to "&lt;video&gt;.speed.csv" in the same format: they
/// are not a complete count and must not enter the counting accuracy.
/// </summary>
public static class GroundTruthFile
{
    private const string Header = "offset_s;motion;class;note;speed_kmh";

    /// <summary>Class column of a <see cref="GroundTruthEntry.SmallVan"/>.</summary>
    public const string SmallVanClass = "SmallVan";

    public static string PathFor(string videoPath) => videoPath + ".gt.csv";

    public static string SpeedPathFor(string videoPath) => videoPath + ".speed.csv";

    /// <summary>Writes via a temporary file, so an interrupted write never leaves a truncated ground truth.</summary>
    public static void Write(string path, IEnumerable<GroundTruthEntry> entries)
    {
        string temporary = path + ".tmp";
        using (var writer = new StreamWriter(temporary, append: false))
        {
            writer.WriteLine(Header);
            foreach (GroundTruthEntry entry in entries.OrderBy(e => e.Offset))
            {
                string note = entry.Note?.Replace(';', ',').ReplaceLineEndings(" ") ?? "";
                writer.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{entry.Offset.TotalSeconds:0.000};{entry.Motion};{(entry.SmallVan && entry.Class == VehicleClass.Van ? SmallVanClass : entry.Class?.ToString() ?? "")};{note};{entry.SpeedKmh?.ToString("0.#", CultureInfo.InvariantCulture) ?? ""}"));
            }
        }

        File.Move(temporary, path, overwrite: true);
    }

    public static List<GroundTruthEntry> Read(string path)
    {
        List<GroundTruthEntry> entries = [];
        int lineNumber = 0;
        foreach (string line in File.ReadLines(path))
        {
            lineNumber++;
            if (lineNumber == 1 || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split(';');
            if (parts.Length < 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                || !Enum.TryParse(parts[1], out ImageMotion motion))
            {
                throw new FormatException($"{path}:{lineNumber}: invalid ground truth line '{line}'.");
            }

            bool smallVan = parts.Length > 2 && parts[2] == SmallVanClass;
            VehicleClass? vehicleClass = smallVan ? VehicleClass.Van
                : parts.Length > 2 && Enum.TryParse(parts[2], out VehicleClass parsed) ? parsed : null;
            string? note = parts.Length > 3 && parts[3].Length > 0 ? parts[3] : null;
            float? speed = parts.Length > 4 && float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float kmh) ? kmh : null;
            entries.Add(new GroundTruthEntry(TimeSpan.FromSeconds(seconds), motion, vehicleClass, note, speed, smallVan));
        }

        return entries;
    }
}

public static class DirectionMapping
{
    /// <summary>
    /// Which <see cref="TravelDirection"/> a movement in the image corresponds to for a given counting line
    /// (A = moving onto the positive, right-hand side of the line, see <see cref="LineSegment.SignedDistance"/>).
    /// </summary>
    public static TravelDirection ToDirection(LineSegment line, ImageMotion motion)
    {
        // d(SignedDistance)/dx = -dy / length: moving right increases the distance if the line points upwards.
        float dy = line.End.Y - line.Start.Y;
        float change = motion == ImageMotion.LeftToRight ? -dy : dy;
        return change switch
        {
            > 0 => TravelDirection.A,
            < 0 => TravelDirection.B,
            _ => throw new ArgumentException("A horizontal counting line cannot separate left and right movement.", nameof(line)),
        };
    }
}
