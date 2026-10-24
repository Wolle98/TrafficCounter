using TrafficCounter.Core;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.Data.Tests;

public sealed class LogRestoreTests
{
    [Fact]
    public void Passages_and_minutes_are_read_from_the_service_log()
    {
        string[] log =
        [
            """2026-10-23 18:30:10.062 +02:00 [DBG] TrafficCounter.Service.CountingWorker: Passage track 3825: "A" "Car" (0.96), 0 trailer(s), line position 0.29, 285 frames, at 18:30:03.861 {"EventId":{"Id":196366089,"Name":"LogPassage"}}""",
            """2026-10-23 18:30:45.193 +02:00 [DBG] TrafficCounter.Service.CountingWorker: Passage track 3834: "B" "Van" (0.55), 1 trailer(s), line position 0.65, 173 frames, at 18:30:41.545 {}""",
            """2026-10-23 18:31:00.071 +02:00 [DBG] TrafficCounter.Service.CountingWorker: Minute 18:30: captured 1499, processed 863, dropped 180, detections 836, passages 3, source "Degraded", 15.1 fps, detector on 46 % of processed frames {}""",
            """2026-10-23 18:31:00.072 +02:00 [DBG] TrafficCounter.Service.CountingWorker: Network 18:30: ping 192.168.1.1 sent 30, lost 0 {}""",
            """not a log line""",
        ];

        (List<LoggedPassage> passages, List<LoggedMinute> minutes) = LogRestore.Parse(log);

        Assert.Equal(2, passages.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-10-23T18:30:03.861+02:00"), passages[0].At);
        Assert.Equal((3825, TravelDirection.A, VehicleClass.Car, 0.96f, (byte)0, 0.29f, 285),
            (passages[0].TrackId, passages[0].Direction, passages[0].Class, passages[0].Confidence, passages[0].Trailers, passages[0].LinePosition, passages[0].Frames));
        Assert.Equal((TravelDirection.B, VehicleClass.Van, (byte)1), (passages[1].Direction, passages[1].Class, passages[1].Trailers));

        LoggedMinute minute = Assert.Single(minutes);
        Assert.Equal(new LoggedMinute(DateTimeOffset.Parse("2026-10-23T18:30:00+02:00"), 1499, 863, 180, 836, 3, SourceState.Degraded), minute);
    }

    [Fact]
    public void A_minute_or_passage_logged_after_midnight_belongs_to_the_day_before()
    {
        string[] log =
        [
            """2026-10-24 00:00:00.049 +02:00 [DBG] TrafficCounter.Service.CountingWorker: Minute 23:59: captured 1500, processed 900, dropped 11, detections 0, passages 0, source "Ok", 15.1 fps {}""",
            """2026-10-24 00:00:02.000 +02:00 [DBG] TrafficCounter.Service.CountingWorker: Passage track 1: "A" "Bicycle" (0.80), 0 trailer(s), line position 0.70, 40 frames, at 23:59:58.100 {}""",
        ];

        (List<LoggedPassage> passages, List<LoggedMinute> minutes) = LogRestore.Parse(log);

        Assert.Equal(DateTimeOffset.Parse("2026-10-23T23:59:00+02:00"), Assert.Single(minutes).Start);
        Assert.Equal(DateTimeOffset.Parse("2026-10-23T23:59:58.1+02:00"), Assert.Single(passages).At);
    }
}
