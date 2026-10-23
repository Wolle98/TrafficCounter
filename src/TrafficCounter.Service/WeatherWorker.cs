using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrafficCounter.Data;

namespace TrafficCounter.Service;

/// <summary>Hourly weather import (ADR-0023 no. 7); off unless a DWD station is configured.</summary>
public sealed class WeatherOptions
{
    /// <summary>DWD station id of the nearest station (private configuration). Empty: no import.</summary>
    public string? StationId { get; set; }

    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Each import fetches this many past hours again, because the DWD completes and corrects them later.</summary>
    public int LookbackHours { get; set; } = 48;
}

/// <summary>
/// Imports the weather of the configured DWD station from Bright Sky into the counter's database. Failures (no
/// internet, API down) are logged and retried at the next interval; counting is never affected.
/// </summary>
internal sealed class WeatherWorker(
    IOptions<ServiceOptions> options, IDbContextFactory<TrafficCounterDbContext> dbFactory, ILogger<WeatherWorker> logger, TimeProvider time)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        WeatherOptions weather = options.Value.Weather;
        if (string.IsNullOrWhiteSpace(weather.StationId))
        {
            return;
        }

        using var http = new HttpClient { BaseAddress = new Uri(WeatherImport.DefaultBaseUrl), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TrafficCounter (+https://github.com/Wolle98/TrafficCounter)");

        // Let the counter start first.
        await Task.Delay(TimeSpan.FromMinutes(2), time, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DateTimeOffset now = time.GetUtcNow();
                await using TrafficCounterDbContext db = await dbFactory.CreateDbContextAsync(stoppingToken);
                int changed = await WeatherImport.ImportAsync(db, http, weather.StationId, now.AddHours(-weather.LookbackHours), now, stoppingToken);
                logger.LogDebug("Weather of station {Station}: {Changed} hours inserted or updated", weather.StationId, changed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Weather import failed: {Message}", ex.Message);
            }

            await Task.Delay(TimeSpan.FromMinutes(Math.Max(5, weather.IntervalMinutes)), time, stoppingToken);
        }
    }
}
