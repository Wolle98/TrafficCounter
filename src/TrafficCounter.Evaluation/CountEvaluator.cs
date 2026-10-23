using TrafficCounter.Core;

namespace TrafficCounter.Evaluation;

/// <summary>A passage reported by the counter, relative to the start of the reference video.</summary>
public sealed record DetectedPassage(TimeSpan Offset, TravelDirection Direction, VehicleClass Class, float? SpeedKmh = null);

public sealed record EvaluationResult(
    int TruthCount,
    int DetectedCount,
    int Matched,
    int DirectionErrors,
    int Missed,
    int Spurious,
    IReadOnlyDictionary<(VehicleClass Truth, VehicleClass Detected), int> ClassConfusion)
{
    /// <summary>Real passages without a counted counterpart, for inspecting the reasons.</summary>
    public IReadOnlyList<GroundTruthEntry> MissedEntries { get; init; } = [];

    /// <summary>Counted passages without a real counterpart.</summary>
    public IReadOnlyList<DetectedPassage> SpuriousPassages { get; init; } = [];

    /// <summary>Share of real passages that were counted (with the right direction).</summary>
    public double Recall => TruthCount == 0 ? 1 : (double)Matched / TruthCount;

    /// <summary>Share of counted passages that correspond to a real one (with the right direction).</summary>
    public double Precision => DetectedCount == 0 ? 1 : (double)Matched / DetectedCount;

    /// <summary>How close the total count is, ignoring timing: 1 - |detected - truth| / truth.</summary>
    public double CountAccuracy => TruthCount == 0 ? (DetectedCount == 0 ? 1 : 0) : Math.Max(0, 1 - (Math.Abs(DetectedCount - TruthCount) / (double)TruthCount));

    /// <summary>Share of matched passages (any direction) whose direction is correct – the stage 2 criterion.</summary>
    public double DirectionAccuracy => Matched + DirectionErrors == 0 ? 1 : (double)Matched / (Matched + DirectionErrors);

    /// <summary>
    /// Counting accuracy used for the acceptance criteria: correct passages relative to real plus extra ones,
    /// so both missed and spurious counts lower it.
    /// </summary>
    public double Accuracy => TruthCount + Spurious == 0 ? 1 : (double)Matched / (TruthCount + Spurious);

    /// <summary>Adds up several results, e.g. all videos of one category.</summary>
    public static EvaluationResult Combine(IEnumerable<EvaluationResult> results)
    {
        List<EvaluationResult> all = [.. results];
        Dictionary<(VehicleClass, VehicleClass), int> confusion = [];
        foreach (var pair in all.SelectMany(r => r.ClassConfusion))
        {
            confusion[pair.Key] = confusion.GetValueOrDefault(pair.Key) + pair.Value;
        }

        return new EvaluationResult(
            all.Sum(r => r.TruthCount),
            all.Sum(r => r.DetectedCount),
            all.Sum(r => r.Matched),
            all.Sum(r => r.DirectionErrors),
            all.Sum(r => r.Missed),
            all.Sum(r => r.Spurious),
            confusion)
        {
            MissedEntries = [.. all.SelectMany(r => r.MissedEntries)],
            SpuriousPassages = [.. all.SelectMany(r => r.SpuriousPassages)],
        };
    }
}

/// <summary>
/// Matches detected passages to ground truth within a time tolerance. Pairs are assigned greedily by smallest
/// time difference, same-direction pairs first; a pair with the wrong direction counts as a direction error.
/// </summary>
public static class CountEvaluator
{
    public static EvaluationResult Evaluate(
        IReadOnlyList<GroundTruthEntry> truth,
        Func<ImageMotion, TravelDirection> toDirection,
        IReadOnlyList<DetectedPassage> detected,
        TimeSpan tolerance)
    {
        bool[] truthUsed = new bool[truth.Count];
        bool[] detectedUsed = new bool[detected.Count];
        Dictionary<(VehicleClass, VehicleClass), int> confusion = [];
        int matched = 0;
        int directionErrors = 0;

        foreach (bool sameDirection in new[] { true, false })
        {
            var candidates =
                from t in Enumerable.Range(0, truth.Count)
                from d in Enumerable.Range(0, detected.Count)
                let difference = (truth[t].Offset - detected[d].Offset).Duration()
                where difference <= tolerance && (toDirection(truth[t].Motion) == detected[d].Direction) == sameDirection
                orderby difference
                select (t, d);

            foreach ((int t, int d) in candidates.ToList())
            {
                if (truthUsed[t] || detectedUsed[d])
                {
                    continue;
                }

                truthUsed[t] = detectedUsed[d] = true;
                if (!sameDirection)
                {
                    directionErrors++;
                    continue;
                }

                matched++;
                if (truth[t].Class is VehicleClass truthClass)
                {
                    var key = (truthClass, detected[d].Class);
                    confusion[key] = confusion.GetValueOrDefault(key) + 1;
                }
            }
        }

        int missed = truthUsed.Count(used => !used);
        int spurious = detectedUsed.Count(used => !used);
        return new EvaluationResult(truth.Count, detected.Count, matched, directionErrors, missed, spurious, confusion)
        {
            MissedEntries = [.. truth.Where((_, i) => !truthUsed[i])],
            SpuriousPassages = [.. detected.Where((_, i) => !detectedUsed[i])],
        };
    }
}
