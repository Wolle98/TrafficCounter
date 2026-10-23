using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TrafficCounter.Data;

namespace TrafficCounter.DemoData;

/// <summary>Recreates the demo database from the migrations and inserts a <see cref="DemoDataSet"/>.</summary>
public static class DemoDataWriter
{
    private const int ChunkSize = 5000;

    /// <summary>
    /// Guard against wiping real data: the database name must contain "Demo".
    /// </summary>
    public static void EnsureDemoDatabase(string connectionString)
    {
        string database = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        if (!database.Contains("demo", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Database '{database}' is not a demo database (name must contain 'Demo'); it would be dropped.");
        }
    }

    public static async Task WriteAsync(
        string connectionString, DemoDataSet data, string cameraName, string directionAName, string directionBName, CancellationToken cancellationToken)
    {
        EnsureDemoDatabase(connectionString);
        DbContextOptions<TrafficCounterDbContext> options = new DbContextOptionsBuilder<TrafficCounterDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using (var db = new TrafficCounterDbContext(options))
        {
            await db.Database.EnsureDeletedAsync(cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
        }

        int cameraId;
        int[] periodIds;
        await using (var db = new TrafficCounterDbContext(options))
        {
            var camera = new Camera
            {
                Name = cameraName,
                Description = "Synthetic demo data (TrafficCounter.DemoData)",
                DirectionAName = directionAName,
                DirectionBName = directionBName,
                CreatedAt = data.Periods[0].StartedAt,
            };
            db.Cameras.Add(camera);
            await db.SaveChangesAsync(cancellationToken);
            cameraId = camera.Id;

            foreach (OperatingPeriod period in data.Periods)
            {
                period.CameraId = cameraId;
            }

            db.OperatingPeriods.AddRange(data.Periods);
            await db.SaveChangesAsync(cancellationToken);
            periodIds = [.. data.Periods.Select(p => p.Id)];
        }

        foreach (VehiclePassage p in data.Passages)
        {
            (p.CameraId, p.OperatingPeriodId) = (cameraId, periodIds[p.OperatingPeriodId]);
        }

        foreach (MinuteStatistic m in data.Minutes)
        {
            (m.CameraId, m.OperatingPeriodId) = (cameraId, periodIds[m.OperatingPeriodId]);
        }

        foreach (SystemEvent e in data.Events)
        {
            (e.CameraId, e.OperatingPeriodId) = (cameraId, periodIds[e.OperatingPeriodId]);
        }

        foreach (DriftMeasurement d in data.DriftMeasurements)
        {
            (d.CameraId, d.OperatingPeriodId) = (cameraId, periodIds[d.OperatingPeriodId]);
        }

        await InsertAsync(options, data.Passages, cancellationToken);
        await InsertAsync(options, data.Minutes, cancellationToken);
        await InsertAsync(options, data.Events, cancellationToken);
        await InsertAsync(options, data.DriftMeasurements, cancellationToken);
    }

    private static async Task InsertAsync<T>(DbContextOptions<TrafficCounterDbContext> options, List<T> rows, CancellationToken cancellationToken)
        where T : class
    {
        foreach (T[] chunk in rows.Chunk(ChunkSize))
        {
            await using var db = new TrafficCounterDbContext(options);
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.Set<T>().AddRange(chunk);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
