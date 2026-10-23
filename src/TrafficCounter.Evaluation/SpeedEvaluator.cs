using TrafficCounter.Core;

namespace TrafficCounter.Evaluation;

/// <summary>One test drive with known speed and the counted passage it was matched to (null if none).</summary>
public sealed record SpeedCheck(GroundTruthEntry Drive, DetectedPassage? Passage)
{
    /// <summary>Measured minus driven speed in km/h; null without a match or without a measured speed.</summary>
    public float? Error => Passage?.SpeedKmh is float measured && Drive.SpeedKmh is float driven ? measured - driven : null;

    /// <summary>Error relative to the driven speed.</summary>
    public double? RelativeError => Error is float error && Drive.SpeedKmh is > 0 and float driven ? error / driven : null;
}

public sealed record SpeedSummary(int Drives, int Measured, double MeanRelativeError, double MeanAbsoluteRelativeError, double MaxAbsoluteRelativeError)
{
    public static SpeedSummary Of(IReadOnlyCollection<SpeedCheck> checks)
    {
        List<double> errors = [.. checks.Select(c => c.RelativeError).OfType<double>()];
        return errors.Count == 0
            ? new SpeedSummary(checks.Count, 0, 0, 0, 0)
            : new SpeedSummary(checks.Count, errors.Count, errors.Average(), errors.Average(Math.Abs), errors.Max(Math.Abs));
    }
}

/// <summary>
/// Matches test drives with known speed (ADR-0035) to counted passages: each drive takes the nearest unused passage in
/// the same direction within the tolerance. Other traffic in the video is not annotated and simply stays unmatched.
/// </summary>
public static class SpeedEvaluator
{
    public static List<SpeedCheck> Evaluate(
        IEnumerable<GroundTruthEntry> truth,
        Func<ImageMotion, TravelDirection> toDirection,
        IReadOnlyList<DetectedPassage> detected,
        TimeSpan tolerance)
    {
        bool[] used = new bool[detected.Count];
        List<SpeedCheck> checks = [];
        foreach (GroundTruthEntry drive in truth.Where(t => t.SpeedKmh is not null).OrderBy(t => t.Offset))
        {
            TravelDirection direction = toDirection(drive.Motion);
            int best = -1;
            for (int i = 0; i < detected.Count; i++)
            {
                TimeSpan difference = (detected[i].Offset - drive.Offset).Duration();
                if (!used[i] && detected[i].Direction == direction && difference <= tolerance
                    && (best < 0 || difference < (detected[best].Offset - drive.Offset).Duration()))
                {
                    best = i;
                }
            }

            if (best >= 0)
            {
                used[best] = true;
            }

            checks.Add(new SpeedCheck(drive, best >= 0 ? detected[best] : null));
        }

        return checks;
    }
}
