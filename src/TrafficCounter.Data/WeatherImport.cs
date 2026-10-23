using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace TrafficCounter.Data;

/// <summary>
/// Imports hourly weather of one DWD station from Bright Sky (https://brightsky.dev, open DWD data, no API key;
/// ADR-0023 no. 7). Only the station id leaves the machine, no coordinates.
/// </summary>
public static class WeatherImport
{
    public const string DefaultBaseUrl = "https://api.brightsky.dev/";

    /// <summary>Fetches the hours from <paramref name="from"/> to <paramref name="to"/> and inserts or updates them.</summary>
    /// <returns>Number of hours inserted or changed.</returns>
    public static async Task<int> ImportAsync(
        TrafficCounterDbContext db, HttpClient http, string stationId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WeatherHour> hours = await FetchAsync(http, stationId, from, to, cancellationToken);
        return await StoreAsync(db, hours, cancellationToken);
    }

    public static async Task<IReadOnlyList<WeatherHour>> FetchAsync(
        HttpClient http, string stationId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        string url = string.Create(CultureInfo.InvariantCulture,
            $"weather?dwd_station_id={Uri.EscapeDataString(stationId)}&date={from.UtcDateTime:yyyy-MM-ddTHH:mm}Z&last_date={to.UtcDateTime:yyyy-MM-ddTHH:mm}Z&tz=Etc/UTC");
        BrightSkyResponse? response = await http.GetFromJsonAsync<BrightSkyResponse>(url, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return [.. (response?.Weather ?? [])
            .Where(w => w.Timestamp <= now)
            .Select(w => new WeatherHour
            {
                StationId = stationId,
                // Precipitation and sunshine are totals of the 60 minutes before the timestamp.
                Hour = w.Timestamp.AddHours(-1),
                PrecipitationMm = w.Precipitation,
                TemperatureC = w.Temperature,
                SunshineMinutes = w.Sunshine,
                WindSpeedKmh = w.WindSpeed,
                CloudCoverPercent = w.CloudCover,
                VisibilityMeters = w.Visibility,
                Condition = w.Condition,
                ImportedAt = now,
            })];
    }

    public static async Task<int> StoreAsync(TrafficCounterDbContext db, IReadOnlyList<WeatherHour> hours, CancellationToken cancellationToken = default)
    {
        if (hours.Count == 0)
        {
            return 0;
        }

        string stationId = hours[0].StationId;
        DateTimeOffset first = hours.Min(h => h.Hour);
        DateTimeOffset last = hours.Max(h => h.Hour);
        Dictionary<DateTimeOffset, WeatherHour> existing = (await db.WeatherHours
            .Where(w => w.StationId == stationId && w.Hour >= first && w.Hour <= last)
            .ToListAsync(cancellationToken))
            .ToDictionary(w => w.Hour.ToUniversalTime());

        int changed = 0;
        foreach (WeatherHour hour in hours)
        {
            if (!existing.TryGetValue(hour.Hour.ToUniversalTime(), out WeatherHour? row))
            {
                db.WeatherHours.Add(hour);
                changed++;
            }
            else if (row.PrecipitationMm != hour.PrecipitationMm || row.TemperatureC != hour.TemperatureC
                || row.SunshineMinutes != hour.SunshineMinutes || row.Condition != hour.Condition)
            {
                // The DWD corrects recent values; the newer import wins.
                row.PrecipitationMm = hour.PrecipitationMm;
                row.TemperatureC = hour.TemperatureC;
                row.SunshineMinutes = hour.SunshineMinutes;
                row.WindSpeedKmh = hour.WindSpeedKmh;
                row.CloudCoverPercent = hour.CloudCoverPercent;
                row.VisibilityMeters = hour.VisibilityMeters;
                row.Condition = hour.Condition;
                row.ImportedAt = hour.ImportedAt;
                changed++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    private sealed record BrightSkyResponse([property: JsonPropertyName("weather")] List<BrightSkyHour>? Weather);

    private sealed record BrightSkyHour(
        [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
        [property: JsonPropertyName("precipitation")] float? Precipitation,
        [property: JsonPropertyName("temperature")] float? Temperature,
        [property: JsonPropertyName("sunshine")] float? Sunshine,
        [property: JsonPropertyName("wind_speed")] float? WindSpeed,
        [property: JsonPropertyName("cloud_cover")] float? CloudCover,
        [property: JsonPropertyName("visibility")] float? Visibility,
        [property: JsonPropertyName("condition")] string? Condition);
}
