using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;
using TrafficCounter.Core.Operations;
using TrafficCounter.Data;
using TrafficCounter.Evaluation;
using TrafficCounter.Pipeline;
using TrafficCounter.Vision;

namespace TrafficCounter.Annotator;

/// <summary>
/// Writes the hand-counted passages of reference videos into the database (ADR-0027), marked as annotation with
/// video name and position. Direction and day/night come from the calibration stored with the video.
/// </summary>
internal static class ImportCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> arguments)
    {
        // --provider PostgreSql writes to the database from TRAFFICCOUNTER_PG instead of SQL Server (ADR-0030).
        DatabaseProvider provider = DatabaseProvider.SqlServer;
        List<string> videos = [];
        for (int i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] == "--provider" && i + 1 < arguments.Count)
            {
                provider = Enum.Parse<DatabaseProvider>(arguments[++i], ignoreCase: true);
            }
            else
            {
                videos.Add(arguments[i]);
            }
        }

        using TrafficCounterDbContext db = TrafficCounterDatabase.Create(provider, PostgresPassword(provider));
        await db.Database.MigrateAsync();
        int failures = 0;
        foreach (string video in videos)
        {
            try
            {
                (AnnotatedRecording recording, SiteOptions site) = Load(video);
                int cameraId = await EnsureCameraAsync(db, site);
                int periodId = await AnnotationImport.ReplaceAsync(db, cameraId, recording, CancellationToken.None);
                db.ChangeTracker.Clear();
                Console.WriteLine(
                    $"{recording.VideoName}: {recording.Passages.Count} passages, {recording.Start:yyyy-MM-dd HH:mm:ss}–{recording.End:HH:mm:ss}, period {periodId}");
            }
            catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
            {
                Console.Error.WriteLine($"{Path.GetFileName(video)}: {ex.Message}");
                failures++;
            }
        }

        return failures == 0 ? 0 : 1;
    }

    /// <summary>Connection string; for PostgreSQL with the password from TRAFFICCOUNTER_PG_PASSWORD if not included.</summary>
    private static string PostgresPassword(DatabaseProvider provider)
    {
        string connection = TrafficCounterDatabase.ConnectionString(provider);
        if (provider != DatabaseProvider.PostgreSql)
        {
            return connection;
        }

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        if (string.IsNullOrEmpty(builder.Password) && PrivateLocations.Get("TRAFFICCOUNTER_PG_PASSWORD") is { } password)
        {
            builder.Password = password;
        }

        return builder.ConnectionString;
    }

    private static (AnnotatedRecording Recording, SiteOptions Site) Load(string video)
    {
        string gtPath = GroundTruthFile.PathFor(video);
        if (!File.Exists(gtPath))
        {
            throw new FileNotFoundException("No ground truth (.gt.csv) next to the video.", gtPath);
        }

        IReadOnlyList<DateTimeOffset> timestamps = FrameTimestampsFile.TryRead(video)
            ?? throw new FileNotFoundException("No frame timestamps (.frames.csv); the recording time is unknown.", FrameTimestampsFile.PathFor(video));

        // The calibration at recording time, as the regression uses it.
        string snapshot = video + ".site.json";
        string sitePath = File.Exists(snapshot) ? snapshot
            : PrivateLocations.ConfigPath ?? throw new FileNotFoundException("No site configuration for the video.", snapshot);
        SiteOptions site = SiteConfigurationFile.Read(sitePath);
        if (ReadBand(video) is { } band)
        {
            site = site.ForCrop(band.Top, band.Bottom);
        }

        Size size;
        using (var capture = new VideoCapture(video, VideoCaptureAPIs.FFMPEG))
        {
            size = new Size(capture.FrameWidth, capture.FrameHeight);
        }

        LineSegment line = site.Line(size);
        var lighting = new LightingClassifier(site.Latitude, site.Longitude);
        DateTimeOffset start = timestamps[0];
        List<AnnotatedPassage> passages =
        [
            .. GroundTruthFile.Read(gtPath).Select(e => new AnnotatedPassage(
                start + e.Offset,
                DirectionMapping.ToDirection(line, e.Motion),
                e.Class,
                e.Offset.TotalSeconds,
                lighting.Classify(start + e.Offset))),
        ];

        float fps = (float)Math.Round((timestamps.Count - 1) / (timestamps[^1] - start).TotalSeconds, 1);
        return (new AnnotatedRecording(Path.GetFileName(video), start, timestamps[^1], fps, passages), site);
    }

    private static async Task<int> EnsureCameraAsync(TrafficCounterDbContext db, SiteOptions site)
    {
        Camera? camera = await db.Cameras.SingleOrDefaultAsync(c => c.Name == site.CameraName);
        if (camera is null)
        {
            camera = new Camera
            {
                Name = site.CameraName,
                DirectionAName = site.DirectionAName,
                DirectionBName = site.DirectionBName,
                CreatedAt = DateTimeOffset.Now,
            };
            db.Cameras.Add(camera);
            await db.SaveChangesAsync();
        }

        return camera.Id;
    }

    private static (float Top, float Bottom)? ReadBand(string video)
    {
        string path = video + ".json";
        if (!File.Exists(path))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("Band", out JsonElement b) && b.ValueKind == JsonValueKind.Object
            ? (b.GetProperty("Top").GetSingle(), b.GetProperty("Bottom").GetSingle())
            : null;
    }
}
