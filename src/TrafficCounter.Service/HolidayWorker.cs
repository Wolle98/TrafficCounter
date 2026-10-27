using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrafficCounter.Data;

namespace TrafficCounter.Service;

/// <summary>Daily import of public and school holidays (ADR-0051); off unless a region is configured.</summary>
public sealed class HolidayOptions
{
    /// <summary>ISO 3166-2 code of the site's region, e.g. "DE-BE" (private configuration). Empty: no import.</summary>
    public string? Subdivision { get; set; }

    public int IntervalHours { get; set; } = 24;
}

/// <summary>
/// Imports the holidays of the configured region from OpenHolidays for last, this and next year. Failures are logged
/// and retried at the next interval; counting is never affected.
/// </summary>
internal sealed class HolidayWorker(
    IOptions<ServiceOptions> options, IDbContextFactory<TrafficCounterDbContext> dbFactory, ILogger<HolidayWorker> logger, TimeProvider time)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        HolidayOptions holidays = options.Value.Holidays;
        if (string.IsNullOrWhiteSpace(holidays.Subdivision))
        {
            return;
        }

        using var http = new HttpClient { BaseAddress = new Uri(HolidayImport.DefaultBaseUrl), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TrafficCounter (+https://github.com/Wolle98/TrafficCounter)");

        // Let the counter start first.
        await Task.Delay(TimeSpan.FromMinutes(3), time, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int year = time.GetLocalNow().Year;
                await using TrafficCounterDbContext db = await dbFactory.CreateDbContextAsync(stoppingToken);
                int changed = await HolidayImport.ImportAsync(db, http, holidays.Subdivision, new DateOnly(year - 1, 1, 1), new DateOnly(year + 1, 12, 31), stoppingToken);
                logger.LogDebug("Holidays of {Subdivision}: {Changed} entries inserted, changed or removed", holidays.Subdivision, changed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Holiday import failed: {Message}", ex.Message);
            }

            await Task.Delay(TimeSpan.FromHours(Math.Max(1, holidays.IntervalHours)), time, stoppingToken);
        }
    }
}
