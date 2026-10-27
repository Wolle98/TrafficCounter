using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;
using Serilog.Events;
using TrafficCounter.Core;
using TrafficCounter.Core.Operations;
using TrafficCounter.Data;
using TrafficCounter.Service;

// appsettings.json lives next to the exe; without this, starting from another directory would miss it.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = CommandLine.Normalize(args),
    ContentRootPath = AppContext.BaseDirectory,
});
ConfigurationSetup.AddPrivateConfiguration(builder.Configuration, PrivateLocations.ConfigPath);

builder.Services.AddWindowsService(o => o.ServiceName = "TrafficCounter");
builder.Services.AddOptions<ServiceOptions>()
    .Bind(builder.Configuration.GetSection(ServiceOptions.SectionName))
    .Validate(o => { o.Site.Validate(); return true; })
    .ValidateOnStart();

ServiceOptions bootOptions = builder.Configuration.GetSection(ServiceOptions.SectionName).Get<ServiceOptions>() ?? new();
string dataDirectory = ConfigurationSetup.DataDirectory(bootOptions.Logging);

builder.Services.AddSerilog((services, logger) =>
{
    LoggingOptions logging = bootOptions.Logging;
    logger
        .ReadFrom.Configuration(builder.Configuration)
        .MinimumLevel.Verbose()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.Console(restrictedToMinimumLevel: logging.ConsoleLevel)
        .WriteTo.File(
            Path.Combine(dataDirectory, "logs", "trafficcounter-.log"),
            restrictedToMinimumLevel: logging.FileLevel,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: logging.RetainedDays,
            fileSizeLimitBytes: 100 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}");

    if (WindowsServiceHelpers.IsWindowsService())
    {
        logger.WriteTo.EventLog("TrafficCounter", manageEventSource: true, restrictedToMinimumLevel: LogEventLevel.Warning);
    }
});

builder.Services.AddSingleton(TimeProvider.System);
string connectionString = builder.Configuration.GetConnectionString("TrafficCounter")
    ?? throw new InvalidOperationException("Connection string 'TrafficCounter' is missing.");
if (bootOptions.Storage.Provider == DatabaseProvider.PostgreSql)
{
    // The password is kept out of the connection string: TRAFFICCOUNTER_PG_PASSWORD (ADR-0030).
    var postgres = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
    if (string.IsNullOrEmpty(postgres.Password) && PrivateLocations.Get(TrafficCounterDatabase.PostgresPasswordVariable) is { } password)
    {
        postgres.Password = password;
    }

    connectionString = postgres.ConnectionString;
    builder.Services.AddDbContextFactory<PostgresTrafficCounterDbContext>(o => o.UseNpgsql(connectionString));
    builder.Services.AddSingleton<IDbContextFactory<TrafficCounterDbContext>, PostgresContextFactoryAdapter>();
}
else
{
    builder.Services.AddDbContextFactory<TrafficCounterDbContext>(o => o.UseSqlServer(connectionString));
}

builder.Services.AddSingleton<ITrafficStore, SqlTrafficStore>();
builder.Services.AddSingleton(new DataPaths(dataDirectory));
builder.Services.AddHostedService<CountingWorker>();
builder.Services.AddHostedService<WeatherWorker>();
builder.Services.AddHostedService<HolidayWorker>();
builder.Services.AddHostedService<CameraSceneWorker>();

try
{
    await builder.Build().RunAsync();
    return Environment.ExitCode;
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    // A non-zero exit code lets the Windows service recovery options restart the service (ADR-0012).
    Log.Fatal(ex, "TrafficCounter terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
