using Microsoft.EntityFrameworkCore;

namespace TrafficCounter.Data;

/// <summary>Database server the counter writes to (ADR-0030).</summary>
public enum DatabaseProvider
{
    /// <summary>Local SQL Server (Express), the default on the development machine.</summary>
    SqlServer,

    /// <summary>PostgreSQL, e.g. on the NAS, for unattended operation on the notebook.</summary>
    PostgreSql,
}

/// <summary>Creates the database context for a provider, for the service and the tools.</summary>
public static class TrafficCounterDatabase
{
    public const string SqlServerVariable = "TRAFFICCOUNTER_DB";
    public const string PostgresVariable = "TRAFFICCOUNTER_PG";
    public const string PostgresPasswordVariable = "TRAFFICCOUNTER_PG_PASSWORD";
    public const string DefaultProviderVariable = "TRAFFICCOUNTER_DB_PROVIDER";

    /// <summary>
    /// Main database of this machine for the analysis app and the tools (not the counting service, which has its own
    /// setting): TRAFFICCOUNTER_DB_PROVIDER (SqlServer or PostgreSql), default SQL Server.
    /// </summary>
    public static DatabaseProvider DefaultProvider =>
        Enum.TryParse(Core.PrivateLocations.Get(DefaultProviderVariable), ignoreCase: true, out DatabaseProvider provider) ? provider : DatabaseProvider.SqlServer;

    /// <summary>
    /// Connection string of a provider for the tools: TRAFFICCOUNTER_DB (SQL Server, default the local instance) or
    /// TRAFFICCOUNTER_PG (PostgreSQL); stored user variables count as well. The PostgreSQL password is best kept out
    /// of the string: Npgsql also reads PGPASSWORD or a pgpass file.
    /// </summary>
    public static string ConnectionString(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer => Core.PrivateLocations.Get(SqlServerVariable) ?? DesignTimeDbContextFactory.DefaultConnectionString,
        _ => Core.PrivateLocations.Get(PostgresVariable)
            ?? throw new InvalidOperationException($"Set {PostgresVariable} to the PostgreSQL connection string, e.g. Host=nas;Port=5432;Database=trafficcounter;Username=trafficcounter."),
    };

    public static TrafficCounterDbContext Create(DatabaseProvider provider, string connectionString) => provider switch
    {
        DatabaseProvider.SqlServer => new TrafficCounterDbContext(
            new DbContextOptionsBuilder<TrafficCounterDbContext>().UseSqlServer(connectionString).Options),
        _ => new PostgresTrafficCounterDbContext(
            new DbContextOptionsBuilder<PostgresTrafficCounterDbContext>().UseNpgsql(connectionString).Options),
    };

    public static TrafficCounterDbContext Create(DatabaseProvider provider) => Create(provider, ConnectionString(provider));
}

/// <summary>Hands out the PostgreSQL context where the base context type is expected (e.g. <see cref="SqlTrafficStore"/>).</summary>
public sealed class PostgresContextFactoryAdapter(IDbContextFactory<PostgresTrafficCounterDbContext> inner)
    : IDbContextFactory<TrafficCounterDbContext>
{
    public TrafficCounterDbContext CreateDbContext() => inner.CreateDbContext();
}

/// <summary>Used by <c>dotnet ef --context PostgresTrafficCounterDbContext</c>; needs no server to add migrations.</summary>
public sealed class PostgresDesignTimeDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<PostgresTrafficCounterDbContext>
{
    public PostgresTrafficCounterDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresTrafficCounterDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable(TrafficCounterDatabase.PostgresVariable) ?? "Host=localhost;Database=trafficcounter")
            .Options);
}
