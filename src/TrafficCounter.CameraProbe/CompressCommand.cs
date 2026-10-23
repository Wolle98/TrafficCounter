using System.Globalization;

using TrafficCounter.Vision;

namespace TrafficCounter.CameraProbe;

/// <summary>ffmpeg, encoder and quality for the H.264 conversion of recordings.</summary>
internal sealed record CompressOptions(string Ffmpeg, string Encoder, int Quality)
{
    public static CompressOptions Parse(Arguments arguments, string ffmpeg)
    {
        string encoder = arguments.Get("encoder") ?? VideoCompressor.DefaultEncoder;
        int quality = arguments.Get("quality") is { } text
            ? int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : VideoCompressor.DefaultQuality;
        _ = VideoCompressor.EncoderArguments(encoder, quality);
        return new CompressOptions(ffmpeg, encoder, quality);
    }

    /// <summary>Null if the recording stays MPEG-4: --compress off, or no ffmpeg found.</summary>
    public static CompressOptions? ForRecording(Arguments arguments)
    {
        string mode = arguments.Get("compress") ?? "auto";
        if (mode is not ("auto" or "on" or "off"))
        {
            throw new UsageException("--compress must be auto, on or off.");
        }

        if (mode == "off")
        {
            return null;
        }

        string? ffmpeg = VideoCompressor.FindFfmpeg(arguments.Get("ffmpeg"));
        if (ffmpeg is null)
        {
            return mode == "on" ? throw new UsageException("--compress on: ffmpeg not found (install it or pass --ffmpeg <path>).") : null;
        }

        return Parse(arguments, ffmpeg);
    }
}

/// <summary>Converts existing recordings to H.264 (same file name; the original is replaced after checking).</summary>
internal static class CompressCommand
{
    public static int Run(Arguments arguments)
    {
        string input = arguments.Get("video") ?? throw new UsageException("compress needs --video <file.mp4|folder>.");
        string ffmpeg = VideoCompressor.FindFfmpeg(arguments.Get("ffmpeg"))
            ?? throw new UsageException("ffmpeg not found (install it or pass --ffmpeg <path>).");
        CompressOptions options = CompressOptions.Parse(arguments, ffmpeg);
        bool keepOriginal = arguments.Get("keep-original") is "true" or "yes" or "on";

        string[] videos = Directory.Exists(input)
            ? Directory.GetFiles(input, "*.mp4").Where(f => !f.EndsWith(".h264.mp4", StringComparison.OrdinalIgnoreCase))
                .Concat(Directory.GetFiles(input, "*.avi")).Order().ToArray()
            : File.Exists(input) ? [Path.GetFullPath(input)] : throw new UsageException($"'{input}' not found.");

        int failed = 0;
        foreach (string video in videos)
        {
            if (VideoCompressor.IsCompressed(video))
            {
                Console.WriteLine($"{Path.GetFileName(video)}: already H.264/HEVC, skipped.");
                continue;
            }

            string target = Path.Combine(Path.GetDirectoryName(video)!, Path.GetFileNameWithoutExtension(video) + ".h264.mp4");
            // An MJPEG recording of the service (.avi) becomes the .mp4 its sidecar files are named after.
            string? output = video.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ? Path.ChangeExtension(video, ".mp4") : null;
            bool replaced = VideoCompressor.Compress(ffmpeg, video, options.Encoder, options.Quality, keepOriginal, output: output);
            if (!replaced && !(keepOriginal && File.Exists(target)))
            {
                failed++;
            }
        }

        return failed == 0 ? 0 : 1;
    }
}
