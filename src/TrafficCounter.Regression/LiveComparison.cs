using Microsoft.EntityFrameworkCore;
using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Data;
using TrafficCounter.Evaluation;
using TrafficCounter.Pipeline;
using TrafficCounter.Vision;

namespace TrafficCounter.Regression;

/// <summary>
/// Compares a hand count with what the running service stored for the same time (ADR-0032): recordings made by the
/// service while it was counting need no second detection run, and the result shows the live behaviour with the
/// counting machine's frame rate and load.
/// </summary>
internal static class LiveComparison
{
    public static async Task<EvaluationResult?> EvaluateAsync(
        string video, SiteOptions site, List<GroundTruthEntry> truth, TimeSpan tolerance, DatabaseProvider provider) =>
        await LoadAsync(video, site, provider) is var (detected, toDirection)
            ? CountEvaluator.Evaluate(truth, toDirection, detected, tolerance)
            : null;

    /// <summary>The passages the service stored during the recording, relative to its first frame, with their speeds.</summary>
    public static async Task<(List<DetectedPassage> Detected, Func<ImageMotion, TravelDirection> ToDirection)?> LoadAsync(
        string video, SiteOptions site, DatabaseProvider provider)
    {
        IReadOnlyList<DateTimeOffset>? timestamps = FrameTimestampsFile.TryRead(video);
        if (timestamps is not { Count: > 1 })
        {
            Console.Error.WriteLine($"{Path.GetFileName(video)}: no .frames.csv, cannot find the recording's time.");
            return null;
        }

        DateTimeOffset start = timestamps[0];
        DateTimeOffset end = timestamps[^1];
        await using TrafficCounterDbContext db = TrafficCounterDatabase.Create(provider);
        var rows = await db.VehiclePassages
            .Where(p => p.Source == DataSource.Counter && p.PassedAt >= start && p.PassedAt <= end)
            .Select(p => new { p.PassedAt, p.Direction, p.VehicleClass, p.SpeedKmh })
            .ToListAsync();
        bool counted = await db.OperatingPeriods.AnyAsync(p =>
            p.Source == DataSource.Counter && p.StartedAt <= start && (p.EndedAt ?? p.LastHeartbeatAt) >= end);
        if (!counted)
        {
            Console.Error.WriteLine($"{Path.GetFileName(video)}: the service was not counting during the whole recording; result incomplete.");
        }

        Size size;
        using (var capture = new VideoCapture(video))
        {
            size = new Size(capture.FrameWidth, capture.FrameHeight);
        }

        List<DetectedPassage> detected = [.. rows.Select(p => new DetectedPassage(p.PassedAt - start, p.Direction, p.VehicleClass ?? VehicleClass.Unknown, p.SpeedKmh))];
        Core.Geometry.LineSegment line = site.Line(size);
        return (detected, m => DirectionMapping.ToDirection(line, m));
    }
}
