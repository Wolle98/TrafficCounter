using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TrafficCounter.Core;

namespace TrafficCounter.Data;

/// <summary>
/// Database of the counter. This class uses SQL Server (ADR-0010); <see cref="PostgresTrafficCounterDbContext"/> the same
/// model on PostgreSQL with its own migrations (ADR-0030).
/// </summary>
public class TrafficCounterDbContext : DbContext
{
    public TrafficCounterDbContext(DbContextOptions<TrafficCounterDbContext> options)
        : base(options)
    {
    }

    protected TrafficCounterDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<Camera> Cameras => Set<Camera>();

    public DbSet<OperatingPeriod> OperatingPeriods => Set<OperatingPeriod>();

    public DbSet<VehiclePassage> VehiclePassages => Set<VehiclePassage>();

    public DbSet<VehicleClassInfo> VehicleClasses => Set<VehicleClassInfo>();

    public DbSet<MinuteStatistic> MinuteStatistics => Set<MinuteStatistic>();

    public DbSet<SystemEvent> SystemEvents => Set<SystemEvent>();

    public DbSet<DriftMeasurement> DriftMeasurements => Set<DriftMeasurement>();

    public DbSet<WeatherHour> WeatherHours => Set<WeatherHour>();

    public DbSet<CalendarEntry> CalendarEntries => Set<CalendarEntry>();

    public DbSet<DayNote> DayNotes => Set<DayNote>();

    /// <summary>
    /// SQL Server keeps local times with their offset; synchronised rows from PostgreSQL (UTC) are converted to local
    /// time, so all rows of one database look alike. The instant is the same either way.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<LocalTimeConverter>();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampPeriods();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampPeriods();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Sets <see cref="OperatingPeriod.UpdatedAt"/> on every change, unless the change sets it itself (sync).</summary>
    private void StampPeriods()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        foreach (var entry in ChangeTracker.Entries<OperatingPeriod>())
        {
            bool setExplicitly = entry.Property(p => p.UpdatedAt).IsModified
                || (entry.State == EntityState.Added && entry.Entity.UpdatedAt != default);
            if (entry.State is EntityState.Added or EntityState.Modified && !setExplicitly)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }

    private sealed class LocalTimeConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToLocalTime(), v => v);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Camera>(e =>
        {
            e.ToTable("Camera");
            e.Property(c => c.Name).HasMaxLength(100);
            e.HasIndex(c => c.Name).IsUnique();
            e.Property(c => c.Description).HasMaxLength(500);
            e.Property(c => c.DirectionAName).HasMaxLength(100);
            e.Property(c => c.DirectionBName).HasMaxLength(100);
            e.Property(c => c.CreatedAt).HasPrecision(3);
        });

        modelBuilder.Entity<OperatingPeriod>(e =>
        {
            e.ToTable("OperatingPeriod");
            e.HasIndex(p => p.SyncId).IsUnique();
            e.Property(p => p.StartedAt).HasPrecision(3);
            e.Property(p => p.UpdatedAt).HasPrecision(3);
            e.Property(p => p.LastHeartbeatAt).HasPrecision(3);
            e.Property(p => p.EndedAt).HasPrecision(3);
            e.Property(p => p.AppVersion).HasMaxLength(50);
            e.Property(p => p.MachineName).HasMaxLength(100);
            e.Property(p => p.ModelName).HasMaxLength(200);
            e.Property(p => p.ConfigHash).HasMaxLength(64);
            e.Property(p => p.SourceVideo).HasMaxLength(260);
            e.HasOne(p => p.Camera).WithMany().HasForeignKey(p => p.CameraId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => new { p.CameraId, p.StartedAt });
        });

        modelBuilder.Entity<VehiclePassage>(e =>
        {
            e.ToTable("VehiclePassage");
            e.HasIndex(p => p.SyncId).IsUnique();
            e.Property(p => p.PassedAt).HasPrecision(3);
            e.HasOne<Camera>().WithMany().HasForeignKey(p => p.CameraId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OperatingPeriod>().WithMany().HasForeignKey(p => p.OperatingPeriodId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<VehicleClassInfo>().WithMany().HasForeignKey(p => p.VehicleClass).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => new { p.CameraId, p.PassedAt });
            e.Property(p => p.SourceVideo).HasMaxLength(260);
            e.HasIndex(p => p.SourceVideo);
        });

        modelBuilder.Entity<MinuteStatistic>(e =>
        {
            e.ToTable("MinuteStatistic");
            e.HasIndex(m => m.SyncId).IsUnique();
            e.Property(m => m.MinuteStart).HasPrecision(0);
            e.HasOne<Camera>().WithMany().HasForeignKey(m => m.CameraId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OperatingPeriod>().WithMany().HasForeignKey(m => m.OperatingPeriodId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(m => new { m.CameraId, m.MinuteStart });
        });

        modelBuilder.Entity<SystemEvent>(e =>
        {
            e.ToTable("SystemEvent");
            e.HasIndex(s => s.SyncId).IsUnique();
            e.Property(s => s.OccurredAt).HasPrecision(3);
            e.Property(s => s.Message).HasMaxLength(1000);
            e.HasOne<Camera>().WithMany().HasForeignKey(s => s.CameraId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OperatingPeriod>().WithMany().HasForeignKey(s => s.OperatingPeriodId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(s => new { s.CameraId, s.OccurredAt });
        });

        modelBuilder.Entity<DriftMeasurement>(e =>
        {
            e.ToTable("DriftMeasurement");
            e.HasIndex(d => d.SyncId).IsUnique();
            e.Property(d => d.MeasuredAt).HasPrecision(0);
            e.HasOne<Camera>().WithMany().HasForeignKey(d => d.CameraId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OperatingPeriod>().WithMany().HasForeignKey(d => d.OperatingPeriodId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(d => new { d.CameraId, d.MeasuredAt });
        });

        modelBuilder.Entity<WeatherHour>(e =>
        {
            e.ToTable("WeatherHour");
            e.Property(w => w.StationId).HasMaxLength(10);
            e.Property(w => w.Hour).HasPrecision(0);
            e.Property(w => w.ImportedAt).HasPrecision(0);
            e.Property(w => w.Condition).HasMaxLength(20);
            e.HasIndex(w => new { w.StationId, w.Hour }).IsUnique();
        });

        modelBuilder.Entity<CalendarEntry>(e =>
        {
            e.ToTable("CalendarEntry");
            e.HasIndex(c => c.ExternalId).IsUnique();
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.EnglishName).HasMaxLength(200);
            e.Property(c => c.Subdivision).HasMaxLength(20);
            e.Property(c => c.ImportedAt).HasPrecision(0);
            e.HasIndex(c => new { c.Subdivision, c.StartDate });
        });

        modelBuilder.Entity<DayNote>(e =>
        {
            e.ToTable("DayNote");
            e.HasIndex(n => n.SyncId).IsUnique();
            e.Property(n => n.Start).HasPrecision(0);
            e.Property(n => n.End).HasPrecision(0);
            e.Property(n => n.Text).HasMaxLength(1000);
            e.Property(n => n.CreatedAt).HasPrecision(3);
            e.Property(n => n.UpdatedAt).HasPrecision(3);
            e.HasIndex(n => n.Start);
        });

        modelBuilder.Entity<VehicleClassInfo>(e =>
        {
            e.ToTable("VehicleClass");
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.Name).HasMaxLength(50);
            e.HasData(
                new VehicleClassInfo { Id = VehicleClass.Unknown, Name = "Unbekannt" },
                new VehicleClassInfo { Id = VehicleClass.Car, Name = "PKW" },
                new VehicleClassInfo { Id = VehicleClass.Van, Name = "Transporter" },
                new VehicleClassInfo { Id = VehicleClass.Truck, Name = "LKW" },
                new VehicleClassInfo { Id = VehicleClass.Bus, Name = "Bus" },
                new VehicleClassInfo { Id = VehicleClass.Motorcycle, Name = "Motorrad/Motorroller" },
                new VehicleClassInfo { Id = VehicleClass.Bicycle, Name = "Fahrrad" },
                new VehicleClassInfo { Id = VehicleClass.Agricultural, Name = "Landwirtschaft & Baufahrzeuge" },
                new VehicleClassInfo { Id = VehicleClass.EScooter, Name = "E-Scooter" });
        });
    }
}
