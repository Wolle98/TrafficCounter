using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TrafficCounter.Core;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.Data.Tests;

/// <summary>
/// Integration tests against a real SQL Server. They run only if TRAFFICCOUNTER_TEST_DB contains a connection
/// string; the database named there is dropped and recreated, so never point it at production data.
/// </summary>
public sealed class SqlTrafficStoreTests : IAsyncLifetime
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("TRAFFICCOUNTER_TEST_DB");
    private readonly ManualTime _time = new(Samples.T0);
    private DbContextOptions<TrafficCounterDbContext> _options = null!;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipWhen(ConnectionString is null, "TRAFFICCOUNTER_TEST_DB not set");
        string database = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        Assert.SkipWhen(!database.Contains("Test", StringComparison.OrdinalIgnoreCase), "TRAFFICCOUNTER_TEST_DB must name a test database");

        _options = new DbContextOptionsBuilder<TrafficCounterDbContext>().UseSqlServer(ConnectionString).Options;
        await using var db = new TrafficCounterDbContext(_options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static readonly CameraRegistration Camera = new("test-cam", "towards centre", "towards station");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SqlTrafficStore CreateStore() => new(new Factory(_options), _time);

    private TrafficCounterDbContext Db() => new(_options);

    private static OperatingPeriodStart Start(DateTimeOffset at) => new("test-cam", at, "1.0.0", "TESTPC", 30, "yolo11s", "abc");

    [Fact]
    public async Task Camera_is_created_once()
    {
        SqlTrafficStore store = CreateStore();
        int first = await store.EnsureCameraAsync(Camera, Ct);
        int second = await store.EnsureCameraAsync(Camera, Ct);

        Assert.Equal(first, second);
        await using TrafficCounterDbContext db = Db();
        Assert.Equal("towards centre", (await db.Cameras.SingleAsync(Ct)).DirectionAName);
    }

    [Fact]
    public async Task Full_run_is_recorded_with_start_minutes_passages_and_orderly_end()
    {
        SqlTrafficStore store = CreateStore();
        int camera = await store.EnsureCameraAsync(Camera, Ct);
        (int period, int unclean) = await store.StartPeriodAsync(camera, Start(Samples.T0), Ct);

        await store.WriteAsync(
            [
                Samples.Passage(1, period) with { CameraId = camera },
                Samples.Minute(0, period) with { CameraId = camera },
                Samples.Minute(1, period) with { CameraId = camera },
            ],
            Ct);
        await store.EndPeriodAsync(period, Samples.T0.AddMinutes(2).AddSeconds(10), PeriodEndReason.Shutdown, Ct);

        await using TrafficCounterDbContext db = Db();
        OperatingPeriod stored = await db.OperatingPeriods.SingleAsync(p => p.Id == period, Ct);
        Assert.Equal(0, unclean);
        Assert.Equal(PeriodEndReason.Shutdown, stored.EndReason);
        Assert.Equal(Samples.T0.AddMinutes(2).AddSeconds(10), stored.EndedAt);
        Assert.Equal(30f, stored.NominalFps);
        Assert.Equal(2, await db.MinuteStatistics.CountAsync(m => m.OperatingPeriodId == period, Ct));
        VehiclePassage passage = await db.VehiclePassages.SingleAsync(Ct);
        Assert.Equal((TravelDirection.A, VehicleClass.Car, 0.4f, (byte)1), (passage.Direction, passage.VehicleClass, passage.LinePosition!.Value, passage.TrailerCount));
        Assert.Equal(
            [SystemEventKind.Started, SystemEventKind.Stopped],
            await db.SystemEvents.OrderBy(e => e.Id).Select(e => e.Kind).ToListAsync(Ct));
    }

    [Fact]
    public async Task Minute_rows_advance_the_heartbeat()
    {
        SqlTrafficStore store = CreateStore();
        int camera = await store.EnsureCameraAsync(Camera, Ct);
        (int period, _) = await store.StartPeriodAsync(camera, Start(Samples.T0), Ct);

        await store.WriteAsync([Samples.Minute(4, period) with { CameraId = camera }], Ct);
        await store.WriteAsync([Samples.Minute(2, period) with { CameraId = camera }], Ct); // older, replayed

        await using TrafficCounterDbContext db = Db();
        Assert.Equal(Samples.T0.AddMinutes(5), (await db.OperatingPeriods.SingleAsync(Ct)).LastHeartbeatAt);
    }

    [Fact]
    public async Task Running_period_of_another_machine_is_left_open()
    {
        SqlTrafficStore store = CreateStore();
        int camera = await store.EnsureCameraAsync(Camera, Ct);
        (int other, _) = await store.StartPeriodAsync(camera, Start(Samples.T0) with { MachineName = "OTHERPC" }, Ct);

        (_, int unclean) = await store.StartPeriodAsync(camera, Start(Samples.T0.AddHours(1)), Ct);

        await using TrafficCounterDbContext db = Db();
        Assert.Equal(0, unclean);
        Assert.Null((await db.OperatingPeriods.SingleAsync(p => p.Id == other, Ct)).EndedAt);
    }

    [Fact]
    public async Task Unclosed_period_is_closed_as_unclean_at_next_start()
    {
        SqlTrafficStore store = CreateStore();
        int camera = await store.EnsureCameraAsync(Camera, Ct);
        (int crashed, _) = await store.StartPeriodAsync(camera, Start(Samples.T0), Ct);
        await store.WriteAsync([Samples.Minute(0, crashed) with { CameraId = camera }], Ct);

        // Power loss: no EndPeriodAsync. Next start one hour later:
        (int next, int unclean) = await store.StartPeriodAsync(camera, Start(Samples.T0.AddHours(1)), Ct);

        await using TrafficCounterDbContext db = Db();
        OperatingPeriod closed = await db.OperatingPeriods.SingleAsync(p => p.Id == crashed, Ct);
        Assert.Equal(1, unclean);
        Assert.NotEqual(crashed, next);
        Assert.Equal(PeriodEndReason.Unclean, closed.EndReason);
        Assert.Equal(Samples.T0.AddMinutes(1), closed.EndedAt); // last heartbeat, not the restart time
        Assert.True(await db.SystemEvents.AnyAsync(e => e.Kind == SystemEventKind.UncleanShutdownDetected, Ct));

        // Minutes buffered during a database outage before the crash arrive later and extend the period.
        await store.WriteAsync([Samples.Minute(5, crashed) with { CameraId = camera }], Ct);
        await using TrafficCounterDbContext db2 = Db();
        Assert.Equal(Samples.T0.AddMinutes(6), (await db2.OperatingPeriods.SingleAsync(p => p.Id == crashed, Ct)).EndedAt);
    }

    [Fact]
    public async Task Drift_measurements_are_stored()
    {
        SqlTrafficStore store = CreateStore();
        int camera = await store.EnsureCameraAsync(Camera, Ct);
        (int period, _) = await store.StartPeriodAsync(camera, Start(Samples.T0), Ct);

        await store.WriteAsync([new DriftRecord(camera, period, Samples.T0, 2, 12.5f, -7f, -0.3f, 1.001f, 240, 0.8f)], Ct);

        await using TrafficCounterDbContext db = Db();
        DriftMeasurement stored = await db.DriftMeasurements.SingleAsync(Ct);
        Assert.Equal((2, 12.5f, -7f, 240), (stored.Status, stored.DxPixels, stored.DyPixels, stored.Matches));
    }

    [Fact]
    public async Task Quality_history_averages_good_minutes_per_hour()
    {
        SqlTrafficStore store = CreateStore();
        int camera = await store.EnsureCameraAsync(Camera, Ct);
        (int period, _) = await store.StartPeriodAsync(camera, Start(Samples.T0), Ct);
        await store.WriteAsync(
            [
                Samples.Minute(0, period) with { CameraId = camera, Contrast = 40, Sharpness = 300 },
                Samples.Minute(1, period) with { CameraId = camera, Contrast = 60, Sharpness = 500 },
                Samples.Minute(2, period) with { CameraId = camera, Contrast = 5, Sharpness = 10, Quality = QualityLevel.Unusable },
                Samples.Minute(3, period) with { CameraId = camera, FramesProcessed = 0 },
            ],
            Ct);

        var history = await store.GetQualityHistoryAsync(camera, Samples.T0.AddDays(-1), Ct);

        var hour = Assert.Single(history);
        Assert.Equal((18, 50f, 400f, 2), (hour.Hour, hour.Contrast, hour.Sharpness, hour.Samples));
    }

    private static AnnotatedRecording Recording(DateTimeOffset start, int passages) => new(
        "20260929-124955.mp4",
        start,
        start.AddMinutes(60),
        30,
        [.. Enumerable.Range(0, passages).Select(i => new AnnotatedPassage(
            start.AddSeconds(10 + i), i % 2 == 0 ? TravelDirection.A : TravelDirection.B, VehicleClass.Van, 10 + i, LightingCondition.Day))]);

    [Fact]
    public async Task Annotated_recording_is_stored_and_replaced_on_reimport()
    {
        int camera = await CreateStore().EnsureCameraAsync(Camera, Ct);
        DateTimeOffset start = Samples.T0.AddHours(5);

        await using (TrafficCounterDbContext db = Db())
        {
            await AnnotationImport.ReplaceAsync(db, camera, Recording(start, 3), Ct);
        }

        await using (TrafficCounterDbContext db = Db())
        {
            await AnnotationImport.ReplaceAsync(db, camera, Recording(start, 2), Ct);
        }

        await using TrafficCounterDbContext check = Db();
        OperatingPeriod period = await check.OperatingPeriods.SingleAsync(Ct);
        Assert.Equal((DataSource.Annotation, "20260929-124955.mp4", start.AddMinutes(60)), (period.Source, period.SourceVideo, period.EndedAt!.Value));
        List<VehiclePassage> passages = await check.VehiclePassages.OrderBy(p => p.PassedAt).ToListAsync(Ct);
        Assert.Equal(2, passages.Count);
        Assert.All(passages, p => Assert.Equal((DataSource.Annotation, period.Id, VehicleClass.Van), (p.Source, p.OperatingPeriodId, p.VehicleClass!.Value)));
        Assert.Equal(11, passages[1].VideoOffsetSeconds);
    }

    [Fact]
    public async Task Annotated_recording_overlapping_a_counter_run_is_refused()
    {
        SqlTrafficStore store = CreateStore();
        int camera = await store.EnsureCameraAsync(Camera, Ct);
        await store.StartPeriodAsync(camera, Start(Samples.T0), Ct);

        await using TrafficCounterDbContext db = Db();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AnnotationImport.ReplaceAsync(db, camera, Recording(Samples.T0.AddMinutes(-30), 1), Ct));
    }

    private sealed class Factory(DbContextOptions<TrafficCounterDbContext> options) : IDbContextFactory<TrafficCounterDbContext>
    {
        public TrafficCounterDbContext CreateDbContext() => new(options);
    }
}
