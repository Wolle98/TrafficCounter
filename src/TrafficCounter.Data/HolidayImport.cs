using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace TrafficCounter.Data;

/// <summary>
/// Imports public and school holidays of one region from OpenHolidays (https://openholidaysapi.org, open data, no API
/// key; ADR-0051). Only the region code (e.g. "DE-BE") leaves the machine.
/// </summary>
public static class HolidayImport
{
    public const string DefaultBaseUrl = "https://openholidaysapi.org/";

    /// <summary>Fetches both kinds for the given days and replaces the stored entries of the region in that range.</summary>
    /// <returns>Number of entries inserted, changed or removed.</returns>
    public static async Task<int> ImportAsync(
        TrafficCounterDbContext db, HttpClient http, string subdivision, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        List<CalendarEntry> entries = [];
        foreach ((CalendarEntryKind kind, string path) in new[] { (CalendarEntryKind.PublicHoliday, "PublicHolidays"), (CalendarEntryKind.SchoolHoliday, "SchoolHolidays") })
        {
            entries.AddRange(await FetchAsync(http, path, kind, subdivision, from, to, cancellationToken));
        }

        return await StoreAsync(db, subdivision, from, to, entries, cancellationToken);
    }

    public static async Task<IReadOnlyList<CalendarEntry>> FetchAsync(
        HttpClient http, string path, CalendarEntryKind kind, string subdivision, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        string country = subdivision.Split('-')[0];
        string url = string.Create(CultureInfo.InvariantCulture,
            $"{path}?countryIsoCode={Uri.EscapeDataString(country)}&subdivisionCode={Uri.EscapeDataString(subdivision)}&validFrom={from:yyyy-MM-dd}&validTo={to:yyyy-MM-dd}");
        List<OpenHoliday> holidays = await http.GetFromJsonAsync<List<OpenHoliday>>(url, cancellationToken) ?? [];
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return [.. holidays.Select(h => new CalendarEntry
        {
            ExternalId = h.Id,
            Kind = kind,
            StartDate = h.StartDate,
            EndDate = h.EndDate,
            Name = NameIn(h, country) ?? NameIn(h, "EN") ?? h.Id.ToString(),
            EnglishName = NameIn(h, "EN"),
            Subdivision = subdivision,
            ImportedAt = now,
        })];
    }

    /// <summary>
    /// Inserts new entries, updates changed ones and removes entries of the region that start in the range but are
    /// no longer listed (a corrected date gets a new id).
    /// </summary>
    public static async Task<int> StoreAsync(
        TrafficCounterDbContext db, string subdivision, DateOnly from, DateOnly to, IReadOnlyList<CalendarEntry> entries, CancellationToken cancellationToken = default)
    {
        Dictionary<Guid, CalendarEntry> existing = await db.CalendarEntries
            .Where(c => c.Subdivision == subdivision && c.EndDate >= from && c.StartDate <= to)
            .ToDictionaryAsync(c => c.ExternalId, cancellationToken);
        HashSet<Guid> listed = [.. entries.Select(e => e.ExternalId)];
        int changed = 0;
        foreach (CalendarEntry entry in entries.DistinctBy(e => e.ExternalId))
        {
            CalendarEntry? row = existing.GetValueOrDefault(entry.ExternalId)
                ?? await db.CalendarEntries.SingleOrDefaultAsync(c => c.ExternalId == entry.ExternalId, cancellationToken);
            if (row is null)
            {
                db.CalendarEntries.Add(entry);
                changed++;
            }
            else if (row.StartDate != entry.StartDate || row.EndDate != entry.EndDate || row.Name != entry.Name
                || row.EnglishName != entry.EnglishName || row.Kind != entry.Kind)
            {
                row.StartDate = entry.StartDate;
                row.EndDate = entry.EndDate;
                row.Name = entry.Name;
                row.EnglishName = entry.EnglishName;
                row.Kind = entry.Kind;
                row.ImportedAt = entry.ImportedAt;
                changed++;
            }
        }

        foreach (CalendarEntry gone in existing.Values.Where(c => !listed.Contains(c.ExternalId) && c.StartDate >= from && c.StartDate <= to))
        {
            db.CalendarEntries.Remove(gone);
            changed++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    private static string? NameIn(OpenHoliday holiday, string language) =>
        holiday.Name?.FirstOrDefault(n => string.Equals(n.Language, language, StringComparison.OrdinalIgnoreCase))?.Text;

    private sealed record OpenHoliday(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("startDate")] DateOnly StartDate,
        [property: JsonPropertyName("endDate")] DateOnly EndDate,
        [property: JsonPropertyName("name")] List<OpenHolidayName>? Name);

    private sealed record OpenHolidayName(
        [property: JsonPropertyName("language")] string Language,
        [property: JsonPropertyName("text")] string Text);
}
