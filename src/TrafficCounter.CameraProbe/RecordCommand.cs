using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Vision;

namespace TrafficCounter.CameraProbe;

/// <summary>
/// Explicit, time-limited recording of reference videos (ADR-0005). Writes only outside the public repository,
/// by default into the private repository's "recordings" folder.
/// </summary>
internal static class RecordCommand
{
    private const string WindowName = "TrafficCounter recording";
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(15);

    /// <param name="fps">Frame rate written into the video: the USB camera's or the IP camera's nominal rate.</param>
    /// <param name="rtsp">The IP camera, if the source is one (for the metadata).</param>
    public static async Task<int> RunAsync(IFrameSource source, double fps, RtspCameraOptions? rtsp, Arguments arguments)
    {
        await using (source)
        {
            return await RecordAsync(source, fps, rtsp, arguments);
        }
    }

    private static async Task<int> RecordAsync(IFrameSource source, double fps, RtspCameraOptions? rtsp, Arguments arguments)
    {
        TimeSpan duration = arguments.Get("minutes") is { } minutes
            ? TimeSpan.FromMinutes(double.Parse(minutes, CultureInfo.InvariantCulture))
            : DefaultDuration;
        if (duration <= TimeSpan.Zero || duration > RecordingPolicy.MaxDuration)
        {
            throw new UsageException($"--minutes must be between 0 and {RecordingPolicy.MaxDuration.TotalMinutes}.");
        }

        string path = ResolveOutputPath(arguments.Get("out"));
        (double Top, double Bottom)? band = arguments.Get("band") is { } bandText ? RecordingPolicy.ParseBand(bandText) : null;
        string[] tags = arguments.Get("tags")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        CompressOptions? compress = CompressOptions.ForRecording(arguments);

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"RECORDING MODE – video with personal data is written to {path}");
        Console.WriteLine($"Duration {duration.TotalMinutes:0.#} min. Keep the file in the private repository only (ADR-0005). Press q to stop early.");
        Console.ResetColor();
        Console.WriteLine(compress is { } plan
            ? $"Afterwards the video is converted to H.264 with {plan.Ffmpeg} ({plan.Encoder})."
            : "No H.264 conversion (ffmpeg not found or --compress off); the MPEG-4 file stays large.");

        source.Start();
        if (!source.TryRead(TimeSpan.FromSeconds(20), out Frame? first))
        {
            Console.Error.WriteLine("No frame from the camera within 20 s.");
            return 1;
        }

        Rect? crop = band is { } b ? RecordingPolicy.BandRect(first!.Image.Size(), b.Top, b.Bottom) : null;
        DateTimeOffset started = first!.Timestamp;
        VideoRecorder recorder;
        using (first)
        {
            recorder = new VideoRecorder(path, first.Image.Size(), fps, crop);
            recorder.Write(first);
        }

        using (recorder)
        {
            Cv2.NamedWindow(WindowName, WindowFlags.AutoSize);
            DateTimeOffset last = started;
            while (last - started < duration)
            {
                if (source.TryRead(TimeSpan.FromSeconds(2), out Frame? frame))
                {
                    using (frame)
                    {
                        recorder.Write(frame!);
                        last = frame!.Timestamp;
                        ShowPreview(frame.Image, crop, duration - (last - started), recorder.FramesWritten);
                    }
                }

                if (Cv2.WaitKey(1) is 'q' or 27)
                {
                    break;
                }
            }

            Cv2.DestroyAllWindows();
            WriteMetadata(path, started, last, recorder.FramesWritten, source, rtsp, band, tags);
            SnapshotCalibration(path);
            Console.WriteLine($"Recorded {recorder.FramesWritten} frames ({(last - started).TotalMinutes:0.0} min), dropped {source.Statistics.Dropped}.");
        }

        if (compress is { } c)
        {
            VideoCompressor.Compress(c.Ffmpeg, path, c.Encoder, c.Quality, keepOriginal: false);
        }

        return 0;
    }

    private static string ResolveOutputPath(string? requested)
    {
        string path;
        if (requested is not null)
        {
            path = Path.GetFullPath(requested);
        }
        else
        {
            string privateRoot = PrivateLocations.PrivateRoot
                ?? throw new UsageException("Set TRAFFICCOUNTER_PRIVATE to the private repository or pass --out <file.mp4>.");
            path = Path.Combine(privateRoot, "recordings", $"{DateTime.Now:yyyyMMdd-HHmmss}.mp4");
        }

        RecordingPolicy.EnsureNotInPublicRepository(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static void ShowPreview(Mat image, Rect? crop, TimeSpan remaining, long frames)
    {
        using Mat region = crop is Rect r ? new Mat(image, r) : image.Clone();
        using Mat display = new();
        double scale = Math.Min(1.0, 960.0 / region.Width);
        Cv2.Resize(region, display, new Size(0, 0), scale, scale, InterpolationFlags.Area);
        Cv2.Circle(display, new Point(20, 20), 8, Scalar.Red, -1);
        string text = $"REC {remaining:mm\\:ss} left  {frames} frames  (q = stop)";
        Cv2.PutText(display, Overlay.Ascii(text), new Point(36, 27), HersheyFonts.HersheySimplex, 0.6, Scalar.Black, 3);
        Cv2.PutText(display, Overlay.Ascii(text), new Point(35, 26), HersheyFonts.HersheySimplex, 0.6, Scalar.White, 1);
        Cv2.ImShow(WindowName, display);
    }

    /// <summary>
    /// Keeps the calibration valid at recording time next to the video, so the regression still uses the right
    /// line and band after the camera has been realigned and recalibrated.
    /// </summary>
    private static void SnapshotCalibration(string videoPath)
    {
        if (PrivateLocations.ConfigPath is { } config && File.Exists(config))
        {
            File.Copy(config, videoPath + ".site.json", overwrite: true);
        }
    }

    private static void WriteMetadata(
        string path, DateTimeOffset started, DateTimeOffset ended, long frames, IFrameSource source, RtspCameraOptions? rtsp,
        (double Top, double Bottom)? band, string[] tags)
    {
        // Never the credentials: only the URL without them and the nominal rate.
        object? camera = rtsp is not null ? new { Rtsp = rtsp.DisplayUrl(), rtsp.Fps, rtsp.UseTcp } : (source as UsbFrameSource)?.ActualSettings;
        var metadata = new
        {
            Note = "Contains personal data – keep in the private repository only (ADR-0005).",
            Started = started,
            Ended = ended,
            Frames = frames,
            source.Statistics.Dropped,
            Band = band is { } b ? new { b.Top, b.Bottom } : null,
            Tags = tags,
            Camera = camera,
        };
        File.WriteAllText(path + ".json", JsonSerializer.Serialize(metadata, ProbeCommand.JsonOptions));
    }
}
