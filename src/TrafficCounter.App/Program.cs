using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using TrafficCounter.App;
using TrafficCounter.App.Components;
using TrafficCounter.App.Data;
using TrafficCounter.Core;
using TrafficCounter.Data;

// Analysis app (stage 4, ADR-0014). Same database settings as the service: Storage:Provider and the connection
// string "TrafficCounter"; the PostgreSQL password comes from TRAFFICCOUNTER_PG_PASSWORD (ADR-0030).
WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Host.UseWindowsService(o => o.ServiceName = "TrafficCounterApp");

DatabaseProvider provider = builder.Configuration.GetValue("Storage:Provider", DatabaseProvider.SqlServer);
string connectionString = builder.Configuration.GetConnectionString("TrafficCounter")
    ?? (provider == DatabaseProvider.SqlServer ? TrafficCounterDatabase.ConnectionString(provider) : null)
    ?? throw new InvalidOperationException("Connection string 'TrafficCounter' is missing.");
if (provider == DatabaseProvider.PostgreSql)
{
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

builder.Services.AddSingleton<TrafficQueries>();
builder.Services.AddScoped<SelectedRange>();
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

WebApplication app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Language: the cookie set by the language selection, else the browser's preference, else English. The Blazor circuit
// takes the culture of the request that opens it, so a change reloads the page.
string[] cultures = [.. AppCultures.Supported.Select(c => c.Name)];
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture(AppCultures.Default)
    .AddSupportedCultures(cultures)
    .AddSupportedUICultures(cultures));
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Language selection: remembers the language for a year and returns to the page (only local addresses).
app.MapGet("/culture", (string name, string? redirect, HttpContext context) =>
{
    if (AppCultures.Supported.Any(c => c.Name == name))
    {
        context.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(name)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax });
    }

    bool local = redirect is { Length: > 0 } && redirect[0] == '/' && !redirect.StartsWith("//", StringComparison.Ordinal)
        && !redirect.StartsWith("/\\", StringComparison.Ordinal);
    return Results.LocalRedirect(local ? redirect! : "/");
});

// Every counted vehicle of a period as CSV (semicolon, opens directly in Excel), local time, in the chosen language.
app.MapGet("/export.csv", async (DateTime from, DateTime to, TrafficQueries queries, IStringLocalizer<Strings> text, CancellationToken cancellationToken) =>
{
    if (!ExportRange.IsValid(from, to))
    {
        return Results.Text(text["Export.InvalidRange", ExportRange.MaxDays].Value, "text/plain; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);
    }

    CameraInfo? camera = await queries.CurrentCameraAsync(cancellationToken);
    if (camera is null)
    {
        return Results.NotFound();
    }

    List<PassageRow> rows = await queries.PassagesAsync(camera.Id, from.Date, to.Date.AddDays(1), cancellationToken);
    var csv = new StringBuilder($"{text["Table.Time"]};{text["Table.Direction"]};{text["Table.Class"]};{text["Export.Speed"]}\r\n");
    foreach (PassageRow row in rows)
    {
        csv.Append(row.At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(';')
            .Append(camera.DirectionName(row.Direction)).Append(';')
            .Append(text.ClassName(row.Class)).Append(';')
            .Append(row.SpeedKmh?.ToString("0.0", CultureInfo.CurrentCulture)).Append("\r\n");
    }

    string name = string.Create(CultureInfo.InvariantCulture, $"{text["Export.FilePrefix"]}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", name);
});

// The same period as an Excel workbook (days, hours with coverage and weather, single vehicles).
app.MapGet("/export.xlsx", async (DateTime from, DateTime to, TrafficQueries queries, IStringLocalizer<Strings> text, CancellationToken cancellationToken) =>
{
    if (!ExportRange.IsValid(from, to))
    {
        return Results.Text(text["Export.InvalidRange", ExportRange.MaxDays].Value, "text/plain; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);
    }

    CameraInfo? camera = await queries.CurrentCameraAsync(cancellationToken);
    if (camera is null)
    {
        return Results.NotFound();
    }

    DateTime start = from.Date;
    DateTime end = to.Date.AddDays(1);
    List<PassageRow> passages = await queries.PassagesAsync(camera.Id, start, end, cancellationToken);
    HashSet<DateTime> covered = TrafficStatistics.CoveredMinutes(await queries.MinutesAsync(camera.Id, start, end, cancellationToken));
    List<WeatherRow> weather = await queries.WeatherAsync(start, end, cancellationToken);
    byte[] workbook = ExcelReport.Create(camera, start, end, passages, covered, weather, text);
    string name = string.Create(CultureInfo.InvariantCulture, $"{text["Export.FilePrefix"]}-{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx");
    return Results.File(workbook, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
});

await app.RunAsync();

/// <summary>Bounds of the export period: one request must not load an unlimited number of rows into memory.</summary>
internal static class ExportRange
{
    public const int MaxDays = 366;

    public static bool IsValid(DateTime from, DateTime to) => to.Date >= from.Date && (to.Date - from.Date).TotalDays < MaxDays;
}

