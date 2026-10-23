using System.Globalization;

namespace TrafficCounter.Vision;

/// <summary>
/// Sidecar file "&lt;video&gt;.frames.csv" with the capture time of every recorded frame.
/// Camera frame intervals vary, so replaying a recording with these timestamps reproduces the original timing
/// exactly instead of assuming a constant frame rate.
/// </summary>
public static class FrameTimestampsFile
{
    private const string Header = "frame;timestamp";

    public static string PathFor(string videoPath) => videoPath + ".frames.csv";

    public static TextWriter CreateWriter(string videoPath)
    {
        var writer = new StreamWriter(PathFor(videoPath), append: false);
        writer.WriteLine(Header);
        return writer;
    }

    public static void Write(TextWriter writer, long frame, DateTimeOffset timestamp) =>
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{frame};{timestamp:O}"));

    /// <summary>Reads the timestamps, or returns null if the video has no sidecar file.</summary>
    public static IReadOnlyList<DateTimeOffset>? TryRead(string videoPath)
    {
        string path = PathFor(videoPath);
        if (!File.Exists(path))
        {
            return null;
        }

        List<DateTimeOffset> timestamps = [];
        foreach (string line in File.ReadLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split(';');
            timestamps.Add(DateTimeOffset.Parse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }

        return timestamps;
    }
}
