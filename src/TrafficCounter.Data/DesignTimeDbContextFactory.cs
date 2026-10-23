using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TrafficCounter.Data;

/// <summary>
/// Used by <c>dotnet ef</c>. Connects to the local default SQL Server instance with Windows authentication
/// unless <c>TRAFFICCOUNTER_DB</c> provides a connection string.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TrafficCounterDbContext>
{
    public const string DefaultConnectionString =
        "Server=localhost;Database=TrafficCounter;Integrated Security=true;TrustServerCertificate=true";

    public TrafficCounterDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("TRAFFICCOUNTER_DB") ?? DefaultConnectionString;
        DbContextOptions<TrafficCounterDbContext> options = new DbContextOptionsBuilder<TrafficCounterDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new TrafficCounterDbContext(options);
    }
}
