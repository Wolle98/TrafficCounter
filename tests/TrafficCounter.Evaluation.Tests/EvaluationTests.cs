using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;

namespace TrafficCounter.Evaluation.Tests;

public sealed class EvaluationTests : IDisposable
{
    // Line drawn from the far kerb (top) to the near kerb (bottom), as in the calibration tool.
    private static readonly LineSegment TopToBottom = new(new Point2(960, 300), new Point2(960, 800));
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(1.5);

    private readonly string _directory = Directory.CreateTempSubdirectory("tc-gt-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static TravelDirection Map(ImageMotion motion) => DirectionMapping.ToDirection(TopToBottom, motion);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Direction_mapping_matches_the_counting_engine_convention()
    {
        // For a top-to-bottom line the positive side is on the left, so right-to-left movement is direction A.
        Assert.Equal(TravelDirection.A, Map(ImageMotion.RightToLeft));
        Assert.Equal(TravelDirection.B, Map(ImageMotion.LeftToRight));

        var bottomToTop = new LineSegment(TopToBottom.End, TopToBottom.Start);
        Assert.Equal(TravelDirection.A, DirectionMapping.ToDirection(bottomToTop, ImageMotion.LeftToRight));
        Assert.Equal(1f, bottomToTop.SignedDistance(new Point2(961, 500))); // right of the line is positive here
    }

    [Fact]
    public void Ground_truth_file_round_trips()
    {
        string path = Path.Combine(_directory, "video.mp4.gt.csv");
        List<GroundTruthEntry> entries =
        [
            new(S(12.345), ImageMotion.RightToLeft, VehicleClass.Car),
            new(S(3.5), ImageMotion.LeftToRight, null, "partly hidden; by bus"),
            new(S(40), ImageMotion.LeftToRight, VehicleClass.Bicycle, "sidewalk"),
        ];

        GroundTruthFile.Write(path, entries);
        List<GroundTruthEntry> read = GroundTruthFile.Read(path);

        Assert.Equal(3, read.Count);
        Assert.Equal(new GroundTruthEntry(S(3.5), ImageMotion.LeftToRight, null, "partly hidden, by bus"), read[0]);
        Assert.Equal(new GroundTruthEntry(S(12.345), ImageMotion.RightToLeft, VehicleClass.Car), read[1]);
        Assert.Equal(VehicleClass.Bicycle, read[2].Class);
    }

    [Fact]
    public void Speed_column_round_trips_and_old_files_without_it_still_read()
    {
        string path = Path.Combine(_directory, "video.mp4.speed.csv");
        GroundTruthFile.Write(path, [new(S(5), ImageMotion.LeftToRight, VehicleClass.Car, "test drive", 30f), new(S(9), ImageMotion.RightToLeft)]);
        List<GroundTruthEntry> read = GroundTruthFile.Read(path);
        Assert.Equal(30f, read[0].SpeedKmh);
        Assert.Null(read[1].SpeedKmh);

        string old = Path.Combine(_directory, "old.gt.csv");
        File.WriteAllLines(old, ["offset_s;motion;class;note", "1.0;LeftToRight;Car;"]);
        Assert.Null(Assert.Single(GroundTruthFile.Read(old)).SpeedKmh);
    }

    [Fact]
    public void Test_drives_take_the_nearest_passage_in_their_direction_and_ignore_other_traffic()
    {
        GroundTruthEntry[] drives =
        [
            new(S(10), ImageMotion.LeftToRight, VehicleClass.Car, null, 30f),
            new(S(50), ImageMotion.RightToLeft, VehicleClass.Car, null, 40f),
            new(S(90), ImageMotion.LeftToRight, VehicleClass.Car, null, 20f),
        ];
        DetectedPassage[] detected =
        [
            new(S(9.9), TravelDirection.A, VehicleClass.Car, 50f), // other traffic, opposite direction
            new(S(10.3), TravelDirection.B, VehicleClass.Car, 31.5f),
            new(S(49.5), TravelDirection.A, VehicleClass.Car, 38f),
            new(S(70), TravelDirection.B, VehicleClass.Car, 30f), // other traffic
        ];

        List<SpeedCheck> checks = SpeedEvaluator.Evaluate(drives, Map, detected, Tolerance);

        Assert.Equal(1.5f, checks[0].Error!.Value, 3);
        Assert.Equal(-2f, checks[1].Error!.Value, 3);
        Assert.Null(checks[2].Passage);
        SpeedSummary summary = SpeedSummary.Of(checks);
        Assert.Equal(2, summary.Measured);
        Assert.Equal((0.05 - 0.05) / 2, summary.MeanRelativeError, 6);
        Assert.Equal(0.05, summary.MeanAbsoluteRelativeError, 6);
    }

    [Fact]
    public void Speedometer_reading_converts_to_true_speed_and_back()
    {
        var calibration = new SpeedometerCalibration(0.987, -1.6);
        Assert.Equal(28.0f, calibration.ToTrue(30), 1);
        Assert.Equal(30f, calibration.ToReading(calibration.ToTrue(30)), 3);
        Assert.True(SpeedometerCalibration.None.IsIdentity);
        Assert.Equal(30f, SpeedometerCalibration.None.ToTrue(30));
    }

    [Fact]
    public void Invalid_line_is_reported_with_its_position()
    {
        string path = Path.Combine(_directory, "bad.gt.csv");
        File.WriteAllLines(path, ["offset_s;motion;class;note", "1.0;Upwards;;"]);
        FormatException ex = Assert.Throws<FormatException>(() => GroundTruthFile.Read(path));
        Assert.Contains(":2:", ex.Message);
    }

    [Fact]
    public void Perfect_detection_scores_one()
    {
        GroundTruthEntry[] truth = [new(S(10), ImageMotion.LeftToRight), new(S(20), ImageMotion.RightToLeft)];
        DetectedPassage[] detected = [new(S(10.4), TravelDirection.B, VehicleClass.Car), new(S(19.8), TravelDirection.A, VehicleClass.Car)];

        EvaluationResult result = CountEvaluator.Evaluate(truth, Map, detected, Tolerance);

        Assert.Equal(2, result.Matched);
        Assert.Equal(1, result.Recall);
        Assert.Equal(1, result.Precision);
        Assert.Equal(1, result.DirectionAccuracy);
    }

    [Fact]
    public void A_small_van_counted_as_car_is_right_and_the_file_keeps_the_mark()
    {
        string path = Path.Combine(_directory, "small.gt.csv");
        GroundTruthFile.Write(path, [new(S(10), ImageMotion.LeftToRight, VehicleClass.Van, SmallVan: true), new(S(20), ImageMotion.LeftToRight, VehicleClass.Van)]);
        Assert.Contains(";SmallVan;", File.ReadAllText(path));
        List<GroundTruthEntry> truth = GroundTruthFile.Read(path);
        Assert.Equal([true, false], truth.Select(e => e.SmallVan));
        Assert.All(truth, e => Assert.Equal(VehicleClass.Van, e.Class));

        DetectedPassage[] detected = [new(S(10.2), TravelDirection.B, VehicleClass.Car), new(S(20.2), TravelDirection.B, VehicleClass.Car)];
        EvaluationResult result = CountEvaluator.Evaluate(truth, Map, detected, Tolerance);

        Assert.Equal(1, result.ToleratedSmallVans);
        ClassScore van = result.ClassScores().Single(c => c.Class == VehicleClass.Van);
        Assert.Equal((2, 1), (van.Truth, van.Correct)); // the large van counted as car stays an error
    }

    [Fact]
    public void Long_vehicles_of_the_ground_truth_get_a_wider_tolerance()
    {
        GroundTruthEntry[] truth =
        [
            new(S(10), ImageMotion.LeftToRight, VehicleClass.Truck),
            new(S(40), ImageMotion.LeftToRight, VehicleClass.Car),
        ];
        DetectedPassage[] detected = [new(S(13.8), TravelDirection.B, VehicleClass.Truck), new(S(43.8), TravelDirection.B, VehicleClass.Car)];

        EvaluationResult result = CountEvaluator.Evaluate(truth, Map, detected, Tolerance);

        Assert.Equal(1, result.Matched);
        Assert.Equal(VehicleClass.Car, Assert.Single(result.MissedEntries).Class);
    }

    [Fact]
    public void Missed_spurious_and_wrong_direction_are_distinguished()
    {
        GroundTruthEntry[] truth =
        [
            new(S(10), ImageMotion.LeftToRight),  // detected correctly
            new(S(30), ImageMotion.LeftToRight),  // detected with wrong direction
            new(S(50), ImageMotion.RightToLeft),  // missed
        ];
        DetectedPassage[] detected =
        [
            new(S(10.2), TravelDirection.B, VehicleClass.Car),
            new(S(30.1), TravelDirection.A, VehicleClass.Car),
            new(S(70), TravelDirection.A, VehicleClass.Car), // spurious
        ];

        EvaluationResult result = CountEvaluator.Evaluate(truth, Map, detected, Tolerance);

        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.DirectionErrors);
        Assert.Equal(1, result.Missed);
        Assert.Equal(1, result.Spurious);
        Assert.Equal(1.0, result.CountAccuracy); // same total – timing-based metrics reveal the errors
        Assert.Equal(1.0 / 3, result.Recall, 6);
        Assert.Equal(0.5, result.DirectionAccuracy);
    }

    [Fact]
    public void Closest_pairs_are_matched_first()
    {
        // Two vehicles 1 s apart in the same direction; detections slightly shifted.
        GroundTruthEntry[] truth = [new(S(10), ImageMotion.LeftToRight), new(S(11), ImageMotion.LeftToRight)];
        DetectedPassage[] detected = [new(S(10.9), TravelDirection.B, VehicleClass.Car), new(S(10.1), TravelDirection.B, VehicleClass.Car)];

        Assert.Equal(2, CountEvaluator.Evaluate(truth, Map, detected, Tolerance).Matched);
    }

    [Fact]
    public void Accuracy_penalises_missed_and_extra_counts_and_results_combine()
    {
        var day = new EvaluationResult(TruthCount: 100, DetectedCount: 98, Matched: 95, DirectionErrors: 1, Missed: 4, Spurious: 2, new Dictionary<(VehicleClass, VehicleClass), int>());
        var night = new EvaluationResult(50, 45, 42, 0, 8, 3, new Dictionary<(VehicleClass, VehicleClass), int> { [(VehicleClass.Car, VehicleClass.Car)] = 40 });

        Assert.Equal(95.0 / 102, day.Accuracy, 6);

        EvaluationResult all = EvaluationResult.Combine([day, night]);
        Assert.Equal((150, 143, 137, 12, 5), (all.TruthCount, all.DetectedCount, all.Matched, all.Missed, all.Spurious));
        Assert.Equal(137.0 / 155, all.Accuracy, 6);
        Assert.Equal(40, all.ClassConfusion[(VehicleClass.Car, VehicleClass.Car)]);
    }

    [Fact]
    public void Class_confusion_is_recorded_for_annotated_entries()
    {
        GroundTruthEntry[] truth = [new(S(5), ImageMotion.LeftToRight, VehicleClass.Van), new(S(15), ImageMotion.LeftToRight)];
        DetectedPassage[] detected = [new(S(5), TravelDirection.B, VehicleClass.Car), new(S(15), TravelDirection.B, VehicleClass.Car)];

        EvaluationResult result = CountEvaluator.Evaluate(truth, Map, detected, Tolerance);

        Assert.Equal(1, result.ClassConfusion[(VehicleClass.Van, VehicleClass.Car)]);
        Assert.Single(result.ClassConfusion);
    }

    [Fact]
    public void Class_scores_and_macro_f1_weight_every_annotated_class_equally()
    {
        var result = new EvaluationResult(14, 14, 14, 0, 0, 0, new Dictionary<(VehicleClass, VehicleClass), int>
        {
            [(VehicleClass.Car, VehicleClass.Car)] = 9,
            [(VehicleClass.Car, VehicleClass.Van)] = 1,
            [(VehicleClass.Van, VehicleClass.Van)] = 1,
            [(VehicleClass.Van, VehicleClass.Car)] = 1,
            [(VehicleClass.Bicycle, VehicleClass.Unknown)] = 1,
            [(VehicleClass.Unknown, VehicleClass.Car)] = 1,
        });

        IReadOnlyList<ClassScore> scores = result.ClassScores();

        Assert.Equal([VehicleClass.Car, VehicleClass.Van, VehicleClass.Bicycle], scores.Select(s => s.Class));
        Assert.Equal(new ClassScore(VehicleClass.Car, 10, 10, 9), scores[0]);
        Assert.Equal(0.9, scores[0].F1, 6);
        Assert.Equal(2.0 / 4, scores[1].F1, 6);
        Assert.Equal(0, scores[2].F1);
        Assert.Equal((0.9 + 0.5 + 0) / 3, result.MacroF1!.Value, 6);
        Assert.Null(new EvaluationResult(0, 0, 0, 0, 0, 0, new Dictionary<(VehicleClass, VehicleClass), int>()).MacroF1);
    }

    [Fact]
    public void Missed_and_extra_passages_are_listed()
    {
        GroundTruthEntry scooter = new(S(30), ImageMotion.LeftToRight, VehicleClass.Bicycle, "e-scooter");
        GroundTruthEntry[] truth = [new(S(10), ImageMotion.LeftToRight, VehicleClass.Car), scooter];
        DetectedPassage ghost = new(S(50), TravelDirection.A, VehicleClass.Truck);
        DetectedPassage[] detected = [new(S(10.2), TravelDirection.B, VehicleClass.Car), ghost];

        EvaluationResult result = EvaluationResult.Combine([CountEvaluator.Evaluate(truth, Map, detected, Tolerance)]);

        Assert.Equal([scooter], result.MissedEntries);
        Assert.Equal([ghost], result.SpuriousPassages);
    }
}
