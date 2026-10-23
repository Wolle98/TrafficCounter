using System.Diagnostics;
using System.Globalization;
using OpenCvSharp;

namespace TrafficCounter.Vision;

/// <summary>
/// Re-encodes a recording to H.264 with an external ffmpeg. OpenCV can only write MPEG-4 Part 2 here (H.264 needs
/// OpenH264, Media Foundation ignores the bitrate), which makes an hour of video several GB. The original is
/// replaced only after the new file has been checked to have the same number of frames, so the
/// "*.frames.csv" timestamps and annotations ("*.gt.csv") stay valid.
/// </summary>
public static class VideoCompressor
{
    public const string DefaultEncoder = "libx264";
    public const int DefaultQuality = 26;

    /// <summary>ffmpeg from --ffmpeg, the PATH or the WinGet links folder (not on the PATH of already running shells).</summary>
    public static string? FindFfmpeg(string? requested)
    {
        if (requested is not null)
        {
            return File.Exists(requested) ? Path.GetFullPath(requested) : throw new ArgumentException($"ffmpeg not found at '{requested}'.");
        }

        IEnumerable<string> folders = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links"));
        return folders.Select(folder => Path.Combine(folder, "ffmpeg.exe")).FirstOrDefault(File.Exists);
    }

    /// <summary>Encoder arguments; quality is the CRF / CQ value (lower = better and larger).</summary>
    public static IReadOnlyList<string> EncoderArguments(string encoder, int quality)
    {
        string q = quality.ToString(CultureInfo.InvariantCulture);
        return encoder switch
        {
            "libx264" => ["-c:v", "libx264", "-preset", "medium", "-crf", q],
            "h264_qsv" => ["-c:v", "h264_qsv", "-preset", "medium", "-global_quality", q, "-look_ahead", "0"],
            "h264_nvenc" => ["-c:v", "h264_nvenc", "-preset", "p5", "-rc", "vbr", "-cq", q, "-b:v", "0"],
            _ => throw new ArgumentException($"Unknown encoder '{encoder}' (libx264, h264_qsv, h264_nvenc)."),
        };
    }

    /// <returns>True if the video was replaced by the H.264 version.</returns>
    /// <param name="output">
    /// Target file; null replaces <paramref name="video"/> itself. With a different target (e.g. an MJPEG recording
    /// in an .avi converted into the .mp4 its sidecar files are named after), the input is deleted afterwards.
    /// </param>
    public static bool Compress(
        string ffmpeg, string video, string encoder, int quality, bool keepOriginal, Action<string>? log = null, bool lowPriority = false,
        string? output = null)
    {
        log ??= Console.WriteLine;
        long frames = CountFrames(video);
        if (frames <= 0)
        {
            log($"{video}: cannot read the frame count, not compressed.");
            return false;
        }

        string target = Path.Combine(Path.GetDirectoryName(video)!, Path.GetFileNameWithoutExtension(video) + ".h264.mp4");
        string temporary = target + ".tmp.mp4";
        List<string> arguments =
        [
            "-hide_banner", "-loglevel", "error", lowPriority ? "-nostats" : "-stats", "-y",
            "-i", video,
            "-map", "0:v:0", "-an",
            // Exactly one output frame per input frame, keyframe every 30 frames for exact seeking in the annotator.
            "-fps_mode", "passthrough", "-g", "30", "-pix_fmt", "yuv420p",
            .. EncoderArguments(encoder, quality),
            "-movflags", "+faststart",
            temporary,
        ];

        log($"{Path.GetFileName(video)}: {frames} frames, {Megabytes(video):N0} MB → H.264 ({encoder}, quality {quality})");
        var stopwatch = Stopwatch.StartNew();
        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using (Process process = Process.Start(start) ?? throw new IOException("ffmpeg could not be started."))
        {
            if (lowPriority)
            {
                // Background conversion next to the counting service: never take CPU time from it.
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                File.Delete(temporary);
                log($"ffmpeg failed (exit code {process.ExitCode}); the original is unchanged.");
                return false;
            }
        }

        long compressedFrames = CountFrames(temporary);
        if (compressedFrames != frames)
        {
            log($"H.264 file has {compressedFrames} instead of {frames} frames; kept as {temporary}, original unchanged.");
            return false;
        }

        log(
            $"  {Megabytes(temporary):N0} MB ({Megabytes(temporary) / Megabytes(video):P0} of the original), {stopwatch.Elapsed:hh\\:mm\\:ss}");
        if (keepOriginal)
        {
            File.Move(temporary, target, overwrite: true);
            log($"  written to {target}; original kept.");
            return false;
        }

        // Same file name, so the sidecar files (*.frames.csv, *.json, *.gt.csv, *.site.json) keep matching.
        File.Move(temporary, output ?? video, overwrite: true);
        if (output is not null && !string.Equals(Path.GetFullPath(output), Path.GetFullPath(video), StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(video);
        }

        return true;
    }

    /// <summary>True if the file is already H.264/HEVC (nothing to gain from re-encoding).</summary>
    public static bool IsCompressed(string video)
    {
        using var capture = new VideoCapture(video);
        string? codec = CameraSettings.DecodeFourCC(capture.Get(VideoCaptureProperties.FourCC))?.ToLowerInvariant();
        return codec is "h264" or "avc1" or "hevc" or "hvc1" or "hev1";
    }

    public static long CountFrames(string video)
    {
        using var capture = new VideoCapture(video);
        return capture.IsOpened() ? (long)capture.Get(VideoCaptureProperties.FrameCount) : -1;
    }

    private static double Megabytes(string file) => new FileInfo(file).Length / 1024.0 / 1024.0;
}
