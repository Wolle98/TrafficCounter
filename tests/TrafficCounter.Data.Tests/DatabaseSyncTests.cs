using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TrafficCounter.Core;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.Data.Tests;

/// <summary>
/// Two-way synchronisation between two SQL Server test databases (the name from TRAFFICCOUNTER_TEST_DB with suffixes
/// "_SyncA" and "_SyncB"). The sync logic is provider independent; PostgreSQL uses the same code path.
/// </summary>
public sealed class DatabaseSyncTests : IAsyncLifetime
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("TRAFFICCOUNTER_TEST_DB");
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 8, 0, 0, TimeSpan.FromHours(2));
    private string _a = null!;
    private string _b = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipWhen(ConnectionString is null, "TRAFFICCOUNTER_TEST_DB not set");
        var builder = new SqlConnectionStringBuilder(ConnectionString);
        Assert.SkipWhen(!builder.InitialCatalog.Contains("Test", StringComparison.OrdinalIgnoreCase), "TRAFFICCOUNTER_TEST_DB must name a test database");
        // Own databases: other test classes recreate the base test database in parallel.
        string catalog = builder.InitialCatalog;
        builder.InitialCatalog = catalog + "_SyncA";
        _a = builder.ConnectionString;
        builder.InitialCatalog = catalog + "_SyncB";
        _b = builder.ConnectionString;
        foreach (string connection in new[] { _a, _b })
        {
            await using TrafficCounterDbContext db = Db(connection);
            await db.Database.EnsureDeletedAsync(Ct);
            await db.Database.MigrateAsync(Ct);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static TrafficCounterDbContext Db(string connection) => TrafficCounterDatabase.Create(DatabaseProvider.SqlServer, connection);

    private static async Task<int> SeedPeriodAsync(string connection, DateTimeOffset start, int passages, string machine)
    {
        await using TrafficCounterDbContext db = Db(connection);
        Camera camera = await db.Cameras.SingleOrDefaultAsync(c => c.Name == "street", Ct)
            ?? db.Cameras.Add(new Camera { Name = "street", CreatedAt = start }).Entity;
        var period = new OperatingPeriod { Camera = camera, StartedAt = start, LastHeartbeatAt = start.AddMinutes(10), AppVersion = "1", MachineName = machine };
        db.OperatingPeriods.Add(period);
        await db.SaveChangesAsync(Ct);
        for (int i = 0; i < passages; i++)
        {
            db.VehiclePassages.Add(new VehiclePassage
            {
                CameraId = camera.Id,
                OperatingPeriodId = period.Id,
                PassedAt = start.AddSeconds(10 * (i + 1)),
                Direction = TravelDirection.A,
                VehicleClass = VehicleClass.Car,
            });
        }

        db.MinuteStatistics.Add(new MinuteStatistic { CameraId = camera.Id, OperatingPeriodId = period.Id, MinuteStart = start, SourceState = SourceState.Ok });
        await db.SaveChangesAsync(Ct);
        return period.Id;
    }

    private async Task SyncBothWaysAsync()
    {
        await using (TrafficCounterDbContext a = Db(_a))
        await using (TrafficCounterDbContext b = Db(_b))
        {
            await DatabaseSync.CopyAsync(a, b, Ct);
        }

        await using (TrafficCounterDbContext a = Db(_a))
        await using (TrafficCounterDbContext b = Db(_b))
        {
            await DatabaseSync.CopyAsync(b, a, Ct);
        }
    }

    [Fact]
    public async Task Both_databases_end_up_with_all_rows_and_a_second_sync_changes_nothing()
    {
        await SeedPeriodAsync(_a, T0, 3, "PC");
        await SeedPeriodAsync(_b, T0.AddDays(1), 5, "NOTEBOOK");

        await SyncBothWaysAsync();

        foreach (string connection in new[] { _a, _b })
        {
            await using TrafficCounterDbContext db = Db(connection);
            Assert.Equal(1, await db.Cameras.CountAsync(Ct));
            Assert.Equal(2, await db.OperatingPeriods.CountAsync(Ct));
            Assert.Equal(8, await db.VehiclePassages.CountAsync(Ct));
            Assert.Equal(2, await db.MinuteStatistics.CountAsync(Ct));
        }

        await using TrafficCounterDbContext source = Db(_a);
        await using TrafficCounterDbContext target = Db(_b);
        SyncResult again = await DatabaseSync.CopyAsync(source, target, Ct);
        Assert.Equal(new SyncResult(0, 0, 0, 0, 0, 0, 0), again);
    }

    [Fact]
    public async Task Newer_period_state_is_carried_over()
    {
        int period = await SeedPeriodAsync(_a, T0, 1, "PC");
        await SyncBothWaysAsync();

        await using (TrafficCounterDbContext a = Db(_a))
        {
            OperatingPeriod p = await a.OperatingPeriods.SingleAsync(x => x.Id == period, Ct);
            p.EndedAt = T0.AddHours(2);
            p.EndReason = PeriodEndReason.Shutdown;
            await a.SaveChangesAsync(Ct);
        }

        await SyncBothWaysAsync();

        await using TrafficCounterDbContext b = Db(_b);
        OperatingPeriod copy = await b.OperatingPeriods.SingleAsync(Ct);
        Assert.Equal((T0.AddHours(2), PeriodEndReason.Shutdown), (copy.EndedAt!.Value, copy.EndReason!.Value));
    }

    [Fact]
    public async Task A_range_copy_takes_only_the_rows_in_the_range()
    {
        await SeedPeriodAsync(_a, T0, 6, "PC"); // passages at +10 s .. +60 s, one minute row at T0
        await SeedPeriodAsync(_a, T0.AddDays(1), 2, "PC"); // outside the range

        await using (TrafficCounterDbContext a = Db(_a))
        await using (TrafficCounterDbContext b = Db(_b))
        {
            SyncResult result = await DatabaseSync.CopyAsync(a, b, (T0, T0.AddSeconds(35)), Ct);
            Assert.Equal((1, 3, 1), (result.PeriodsAdded, result.PassagesAdded, result.MinutesAdded));
        }

        // Repeating it adds nothing; a later range of the same period adds the rest.
        await using (TrafficCounterDbContext a = Db(_a))
        await using (TrafficCounterDbContext b = Db(_b))
        {
            Assert.Equal(0, (await DatabaseSync.CopyAsync(a, b, (T0, T0.AddSeconds(35)), Ct)).PassagesAdded);
            Assert.Equal(3, (await DatabaseSync.CopyAsync(a, b, (T0.AddSeconds(35), T0.AddMinutes(5)), Ct)).PassagesAdded);
        }
    }

    /// <summary>A period of one machine with one minute row per state and <paramref name="perMinute"/> passages in each minute.</summary>
    private static async Task SeedMinutesAsync(string connection, string machine, SourceState[] states, int perMinute)
    {
        await using TrafficCounterDbContext db = Db(connection);
        Camera camera = await db.Cameras.SingleOrDefaultAsync(c => c.Name == "street", Ct)
            ?? db.Cameras.Add(new Camera { Name = "street", CreatedAt = T0 }).Entity;
        var period = new OperatingPeriod { Camera = camera, StartedAt = T0, LastHeartbeatAt = T0.AddMinutes(states.Length), AppVersion = "1", MachineName = machine };
        db.OperatingPeriods.Add(period);
        await db.SaveChangesAsync(Ct);
        for (int minute = 0; minute < states.Length; minute++)
        {
            db.MinuteStatistics.Add(new MinuteStatistic { CameraId = camera.Id, OperatingPeriodId = period.Id, MinuteStart = T0.AddMinutes(minute), SourceState = states[minute] });
            for (int i = 0; i < perMinute; i++)
            {
                db.VehiclePassages.Add(new VehiclePassage
                {
                    CameraId = camera.Id, OperatingPeriodId = period.Id, PassedAt = T0.AddMinutes(minute).AddSeconds(10 + (20 * i)),
                    Direction = TravelDirection.A, VehicleClass = VehicleClass.Car,
                });
            }
        }

        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Gap_fill_replaces_the_impaired_minutes_with_the_second_camera_and_nothing_twice()
    {
        // Target: counting camera measured minutes 0-1, degraded 2-3, unavailable 4, no row for minute 5 (not synced
        // yet or the machine was off); one passage per minute.
        // Source: the second camera measured all five minutes, two passages per minute.
        SourceState[] counting = [SourceState.Ok, SourceState.Ok, SourceState.Degraded, SourceState.Degraded, SourceState.Unavailable];
        await SeedMinutesAsync(_b, "NOTEBOOK", counting, perMinute: 1);
        await SeedMinutesAsync(_a, "PC", [.. Enumerable.Repeat(SourceState.Ok, 6)], perMinute: 2);

        await using (TrafficCounterDbContext source = Db(_a))
        await using (TrafficCounterDbContext target = Db(_b))
        {
            GapFillResult dry = await GapFill.FillAsync(source, target, T0, T0.AddMinutes(6), dryRun: true, Ct);
            Assert.Equal((3, 1, 3, 3), (dry.Minutes, dry.Ranges, dry.PassagesRemoved, dry.MinutesRemoved));

            GapFillResult result = await GapFill.FillAsync(source, target, T0, T0.AddMinutes(6), dryRun: false, Ct);
            Assert.Equal((6, 3), (result.Copied!.PassagesAdded, result.Copied.MinutesAdded)); // minute 5 left alone
        }

        await using (TrafficCounterDbContext db = Db(_b))
        {
            Assert.Equal(2 + 6, await db.VehiclePassages.CountAsync(Ct));          // minutes 0-1 kept, 2-4 from the second camera
            Assert.Equal(5, await db.MinuteStatistics.CountAsync(Ct));             // one row per minute, all measured
            Assert.All(await db.MinuteStatistics.ToListAsync(Ct), m => Assert.Equal(SourceState.Ok, m.SourceState));
        }

        // Running it again finds nothing left to fill.
        await using (TrafficCounterDbContext source = Db(_a))
        await using (TrafficCounterDbContext target = Db(_b))
        {
            Assert.Equal(0, (await GapFill.FillAsync(source, target, T0, T0.AddMinutes(6), dryRun: false, Ct)).Minutes);

            // Only on request also the minute without any row.
            Assert.Equal(1, (await GapFill.FillAsync(source, target, T0, T0.AddMinutes(6), dryRun: true, Ct, includeMissing: true)).Minutes);
        }
    }

    [Fact]
    public async Task Gap_fill_skips_minutes_the_second_camera_did_not_measure_either()
    {
        await SeedMinutesAsync(_b, "NOTEBOOK", [SourceState.Degraded, SourceState.Degraded], perMinute: 1);
        await SeedMinutesAsync(_a, "PC", [SourceState.Ok, SourceState.Degraded], perMinute: 1);

        await using TrafficCounterDbContext source = Db(_a);
        await using TrafficCounterDbContext target = Db(_b);
        GapFillResult result = await GapFill.FillAsync(source, target, T0, T0.AddMinutes(2), dryRun: false, Ct);

        Assert.Equal(1, result.Minutes); // minute 1 stays "not measured": both cameras were impaired
        Assert.Equal(1, await target.MinuteStatistics.CountAsync(m => m.SourceState == SourceState.Degraded, Ct));
    }

    private static WeatherHour Weather(DateTimeOffset hour, float temperature, DateTimeOffset importedAt) =>
        new() { StationId = "00000", Hour = hour, TemperatureC = temperature, ImportedAt = importedAt };

    [Fact]
    public async Task Backfilled_weather_hours_are_carried_over_although_imported_earlier()
    {
        // B imports today's hours; A then backfills last week with an older import time (e.g. a clock behind).
        await using (TrafficCounterDbContext b = Db(_b))
        {
            await WeatherImport.StoreAsync(b, [Weather(T0, 15, T0.AddHours(2))], Ct);
        }

        await using (TrafficCounterDbContext a = Db(_a))
        {
            await WeatherImport.StoreAsync(a, [Weather(T0.AddDays(-7), 9, T0.AddHours(1)), Weather(T0, 14, T0.AddHours(1))], Ct);
        }

        await SyncBothWaysAsync();

        foreach (string connection in new[] { _a, _b })
        {
            await using TrafficCounterDbContext db = Db(connection);
            List<WeatherHour> hours = await db.WeatherHours.OrderBy(w => w.Hour).ToListAsync(Ct);
            Assert.Equal([9f, 15f], hours.Select(w => w.TemperatureC!.Value)); // backfill kept, newer import of T0 won
        }
    }

    [Fact]
    public async Task Reimported_recording_replaces_the_older_import_on_the_other_side()
    {
        AnnotatedRecording Recording(int passages) => new(
            "20260929-124955.mp4", T0, T0.AddHours(1), 30,
            [.. Enumerable.Range(0, passages).Select(i => new AnnotatedPassage(T0.AddSeconds(i + 1), TravelDirection.B, VehicleClass.Van, i + 1, LightingCondition.Day))]);

        await using (TrafficCounterDbContext a = Db(_a))
        {
            a.Cameras.Add(new Camera { Name = "street", CreatedAt = T0 });
            await a.SaveChangesAsync(Ct);
            await AnnotationImport.ReplaceAsync(a, (await a.Cameras.SingleAsync(Ct)).Id, Recording(4), Ct);
        }

        await SyncBothWaysAsync();

        await using (TrafficCounterDbContext a = Db(_a))
        {
            await AnnotationImport.ReplaceAsync(a, (await a.Cameras.SingleAsync(Ct)).Id, Recording(2), Ct);
        }

        await SyncBothWaysAsync();

        foreach (string connection in new[] { _a, _b })
        {
            await using TrafficCounterDbContext db = Db(connection);
            Assert.Equal(1, await db.OperatingPeriods.CountAsync(Ct));
            Assert.Equal(2, await db.VehiclePassages.CountAsync(Ct));
        }
    }
}
