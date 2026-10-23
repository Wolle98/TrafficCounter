using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TrafficCounter.Data;

/// <summary>
/// The counter database on PostgreSQL, e.g. on the NAS (ADR-0030). Same model as on SQL Server, own migrations in
/// Migrations/Postgres. PostgreSQL stores "timestamp with time zone" as UTC and Npgsql accepts only UTC offsets, so
/// all times are converted to UTC when written; they come back as UTC and are shown in local time by the reader.
/// </summary>
public sealed class PostgresTrafficCounterDbContext(DbContextOptions<PostgresTrafficCounterDbContext> options)
    : TrafficCounterDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcConverter>();
    }

    private sealed class UtcConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v);
}
