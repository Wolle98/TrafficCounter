using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TrafficCounter.Core;
using TrafficCounter.Data;

Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
for (int i = 1; i + 1 < args.Length; i += 2)
{
    options[args[i].TrimStart('-')] = args[i + 1];
}

const string PasswordVariable = "TRAFFICCOUNTER_PG_PASSWORD";

switch (args.FirstOrDefault())
{
    case "setup-postgres":
        return await SetupPostgresAsync();
    case "check-postgres":
        return await CheckPostgresAsync();
    case "migrate":
        return await MigrateAsync(Enum.Parse<DatabaseProvider>(options.GetValueOrDefault("provider", "PostgreSql"), ignoreCase: true));
    case "sync":
        return await SyncAsync(options.GetValueOrDefault("direction", "both"), RangeFrom(options));
    case "rescale-speed":
        return await RescaleSpeedAsync(options);
    case "mark-unmeasured":
        return await MarkUnmeasuredAsync(options);
    case "delete-passages":
        return await CorrectPassagesAsync(options, newClass: null);
    case "set-class":
        return await CorrectPassagesAsync(options, Enum.Parse<VehicleClass>(options["class"], ignoreCase: true));
    case "set-trailers":
        return await SetTrailersAsync(options);
    case "add-alongside":
        return await AddAlongsideAsync(options);
    case "fill-degraded":
        return await FillDegradedAsync(options);
    case "restore-from-log":
        return await RestoreFromLogAsync(options);
    case "weather":
        return await WeatherAsync(
            options.TryGetValue("provider", out string? weatherProvider) ? Enum.Parse<DatabaseProvider>(weatherProvider, ignoreCase: true) : TrafficCounterDatabase.DefaultProvider,
            options.GetValueOrDefault("station") ?? SiteSetting("Weather", "StationId"),
            options.GetValueOrDefault("from"),
            options.GetValueOrDefault("to"));
    case "holidays":
        return await HolidaysAsync(options);
    case "list-notes":
        return await ListNotesAsync(options);
    case "add-note":
        return await AddNoteAsync(options);
    case "delete-note":
        return await DeleteNoteAsync(options);
    default:
        Console.WriteLine(
            $"""
            TrafficCounter sync – PostgreSQL (e.g. on the NAS) next to SQL Server (ADR-0030).

            Usage:
              TrafficCounter.Sync setup-postgres --host <nas> [--port 5432] [--admin pgadmin] [--database trafficcounter] [--role trafficcounter]
                  Creates the login role and the database, then applies the migrations as that role.
                  Passwords come from environment variables, never from the command line:
                    PGPASSWORD          password of the admin (read by Npgsql),
                    {PasswordVariable}  password for the new role (choose one).
              TrafficCounter.Sync check-postgres --host <nas> [--port 5432] [--admin pgadmin]
                  Only connects (password from PGPASSWORD) and lists version, databases and roles; changes nothing.
              TrafficCounter.Sync migrate [--provider PostgreSql|SqlServer]
              TrafficCounter.Sync sync [--direction both|to-postgres|to-sqlserver]
                  Copies missing rows in the given direction(s); run it whenever both databases are reachable.
              TrafficCounter.Sync sync --direction to-postgres|to-sqlserver --from <time> --to <time>
                  Only rows in that time range, e.g. to fill a gap of the counting machine with the data of a second
                  camera from another database ({TrafficCounterDatabase.SqlServerVariable}); copying the rest of it later would count twice.
              TrafficCounter.Sync rescale-speed --from <time> --to <time> --old <far>/<near> --new <far>/<near>
                                       --far-kerb <pos> --near-kerb <pos> [--machine <name>] [--old-interpolation linear-meters] [--dry-run true]
                  Recomputes stored speeds after a new speed scale (meters per image width at the kerbs), in both
                  databases; --machine limits it to one counting machine's periods (not passages copied in from a
                  second camera); --old-interpolation linear-meters for speeds stored before ADR-0034.
              TrafficCounter.Sync mark-unmeasured --from <time> --to <time> [--dry-run true]
                  Marks the minutes of a period as not measured (source unavailable) in both databases, e.g. while
                  a stalled camera delivered only black frames; the analysis then treats them as a gap.
              TrafficCounter.Sync delete-passages --sync-ids <id>[,<id>...] [--dry-run true]
              TrafficCounter.Sync set-class --sync-ids <id>[,<id>...] --class <class> [--dry-run true]
                  Corrects single counted passages in both databases, e.g. a pram counted as a motorcycle (delete)
                  or two bicycles counted as a motorcycle (class). The id is the passage's SyncId, the same in both.
              TrafficCounter.Sync set-trailers --sync-ids <id>[,<id>...] --trailers <n> [--dry-run true]
                  Corrects the number of trailers of single passages in both databases, e.g. a cyclist riding behind
                  another that was taken for a trailer (then add-alongside adds the cyclist).
              TrafficCounter.Sync add-alongside --sync-id <id> [--class <class>]
                  Adds a vehicle the counter merged into this passage (e.g. the second of two cyclists side by side):
                  same time, direction and period, in PostgreSQL; the next sync copies it on.
              TrafficCounter.Sync fill-degraded --from <time> --to <time> --source-db <SQL Server connection> [--dry-run true] [--include-missing true]
                  Fills the minutes the counting camera stored as not measured (stream or view impaired, unavailable)
                  with the data of a second camera from its own database, in both databases. Only minutes the second
                  camera measured itself; the impaired camera's passages of those minutes are removed first, so nothing
                  counts twice. Run a sync first. --include-missing also fills minutes without any row (e.g. the
                  counting machine was off); only after a sync, otherwise not yet copied minutes would count twice.
              TrafficCounter.Sync restore-from-log --log <file>[,<file>...] --machine <name> --from <time> --to <time> [--dry-run true]
                  Puts a counting machine's passages and minutes back from its service log (debug level) for the
                  minutes the databases no longer have, e.g. after fill-degraded replaced them with a second camera's
                  counts; that camera's rows of those minutes are removed. The log has no speeds and no image quality.
              TrafficCounter.Sync weather [--provider SqlServer|PostgreSql] [--station <DWD id>] [--from yyyy-MM-dd] [--to yyyy-MM-dd]
                  Imports hourly weather of a DWD station from Bright Sky (ADR-0023); default: the last 7 days and
                  the station in TrafficCounter:Weather:StationId of the private site configuration.
              TrafficCounter.Sync holidays [--provider SqlServer|PostgreSql] [--subdivision <code>] [--from-year <yyyy>] [--to-year <yyyy>]
                  Imports public and school holidays of a region from OpenHolidays (ADR-0051); default: last, this
                  and next year and the region in TrafficCounter:Holidays:Subdivision (e.g. DE-BE) of the site configuration.
              TrafficCounter.Sync list-notes [--from yyyy-MM-dd] [--to yyyy-MM-dd]
              TrafficCounter.Sync add-note --from <day or time> --to <day or time> --text <text>
              TrafficCounter.Sync delete-note --id <sync id>
                  Notes on time spans shown in the analysis app (ADR-0051), e.g. a building site; in PostgreSQL, the
                  next sync copies them on. A day without a time means the whole day: --from 2026-10-27 --to 2026-10-31
                  covers 27 to 31 October. list-notes shows the last 30 days and the coming 30 by default.

            Connections: {TrafficCounterDatabase.SqlServerVariable} (SQL Server, default the local instance) and
            {TrafficCounterDatabase.PostgresVariable} (PostgreSQL, without password), password from {PasswordVariable}.
            Stored user variables are used as well.
            """);
        return args.Length == 0 ? 0 : 2;
}

// PostgreSQL connection of the counter role; the password is added from the environment if missing.
static string PostgresConnection()
{
    var builder = new NpgsqlConnectionStringBuilder(TrafficCounterDatabase.ConnectionString(DatabaseProvider.PostgreSql));
    if (string.IsNullOrEmpty(builder.Password) && PrivateLocations.Get(PasswordVariable) is { } password)
    {
        builder.Password = password;
    }

    return builder.ConnectionString;
}

static TrafficCounterDbContext Open(DatabaseProvider provider) => TrafficCounterDatabase.Create(
    provider, provider == DatabaseProvider.PostgreSql ? PostgresConnection() : TrafficCounterDatabase.ConnectionString(provider));

async Task<int> SetupPostgresAsync()
{
    string host = options.GetValueOrDefault("host") ?? throw new ArgumentException("--host is required.");
    string database = options.GetValueOrDefault("database", "trafficcounter");
    string role = options.GetValueOrDefault("role", "trafficcounter");
    string rolePassword = PrivateLocations.Get(PasswordVariable)
        ?? throw new InvalidOperationException($"Set {PasswordVariable} to the password for the role '{role}' first.");
    var admin = new NpgsqlConnectionStringBuilder
    {
        Host = host,
        Port = int.Parse(options.GetValueOrDefault("port", "5432"), System.Globalization.CultureInfo.InvariantCulture),
        Username = options.GetValueOrDefault("admin", "pgadmin"),
        Database = "postgres",
    };

    await using (var connection = new NpgsqlConnection(admin.ConnectionString))
    {
        await connection.OpenAsync();
        Console.WriteLine($"Connected to {host} as {admin.Username}: {connection.PostgreSqlVersion}");

        if (await ScalarAsync(connection, "SELECT 1 FROM pg_roles WHERE rolname = @name", role) is null)
        {
            // Identifiers cannot be parameters; the role name is validated, the password is a quoted literal.
            await ExecuteAsync(connection, $"CREATE ROLE {Identifier(role)} LOGIN PASSWORD {Literal(rolePassword)}");
            Console.WriteLine($"Role {role} created");
        }
        else
        {
            await ExecuteAsync(connection, $"ALTER ROLE {Identifier(role)} LOGIN PASSWORD {Literal(rolePassword)}");
            Console.WriteLine($"Role {role} exists, password updated");
        }

        if (await ScalarAsync(connection, "SELECT 1 FROM pg_database WHERE datname = @name", database) is null)
        {
            await ExecuteAsync(connection, $"CREATE DATABASE {Identifier(database)} OWNER {Identifier(role)} ENCODING 'UTF8'");
            Console.WriteLine($"Database {database} created");
        }
    }

    var counter = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Username = role, Database = database, Password = rolePassword };
    await using (TrafficCounterDbContext db = TrafficCounterDatabase.Create(DatabaseProvider.PostgreSql, counter.ConnectionString))
    {
        await db.Database.MigrateAsync();
    }

    counter.Password = null;
    Console.WriteLine("Migrations applied. Connection for the tools (password in " + PasswordVariable + "):");
    Console.WriteLine($"  {TrafficCounterDatabase.PostgresVariable}={counter.ConnectionString}");
    return 0;
}

async Task<int> CheckPostgresAsync()
{
    var admin = new NpgsqlConnectionStringBuilder
    {
        Host = options.GetValueOrDefault("host") ?? throw new ArgumentException("--host is required."),
        Port = int.Parse(options.GetValueOrDefault("port", "5432"), System.Globalization.CultureInfo.InvariantCulture),
        Username = options.GetValueOrDefault("admin", "pgadmin"),
        Database = "postgres",
        Timeout = 5,
    };
    try
    {
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        Console.WriteLine($"{admin.Host}:{admin.Port} as {admin.Username}: connected, PostgreSQL {connection.PostgreSqlVersion}");
        await using var command = new NpgsqlCommand(
            "SELECT 'database ' || datname FROM pg_database WHERE NOT datistemplate UNION ALL SELECT 'role ' || rolname FROM pg_roles WHERE left(rolname, 3) <> 'pg_'",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Console.WriteLine("  " + reader.GetString(0));
        }

        return 0;
    }
    catch (Exception ex) when (ex is NpgsqlException or PostgresException or TimeoutException)
    {
        Console.WriteLine($"{admin.Host}:{admin.Port} as {admin.Username}: {ex.Message}");
        return 1;
    }
}

static async Task<int> MigrateAsync(DatabaseProvider provider)
{
    await using TrafficCounterDbContext db = Open(provider);
    await db.Database.MigrateAsync();
    Console.WriteLine($"{provider}: migrations applied");
    return 0;
}

static (DateTimeOffset From, DateTimeOffset To)? RangeFrom(Dictionary<string, string> options) =>
    options.TryGetValue("from", out string? from) && options.TryGetValue("to", out string? to)
        ? (DateTimeOffset.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.Parse(to, System.Globalization.CultureInfo.InvariantCulture))
        : null;

static async Task<int> SyncAsync(string direction, (DateTimeOffset From, DateTimeOffset To)? range)
{
    if (range is not null && direction is not ("to-postgres" or "to-sqlserver"))
    {
        Console.Error.WriteLine("--from/--to need one direction (to-postgres or to-sqlserver).");
        return 2;
    }

    var watch = Stopwatch.StartNew();
    foreach ((DatabaseProvider from, DatabaseProvider to) in direction switch
    {
        "to-postgres" => new[] { (DatabaseProvider.SqlServer, DatabaseProvider.PostgreSql) },
        "to-sqlserver" => [(DatabaseProvider.PostgreSql, DatabaseProvider.SqlServer)],
        _ => [(DatabaseProvider.SqlServer, DatabaseProvider.PostgreSql), (DatabaseProvider.PostgreSql, DatabaseProvider.SqlServer)],
    })
    {
        await using TrafficCounterDbContext source = Open(from);
        await using TrafficCounterDbContext target = Open(to);
        // Both sides need the same schema (new tables such as WeatherHour).
        await source.Database.MigrateAsync();
        await target.Database.MigrateAsync();
        SyncResult result = await DatabaseSync.CopyAsync(source, target, range, CancellationToken.None);
        Console.WriteLine($"{from} -> {to}: {result}");
    }

    Console.WriteLine($"Done in {watch.Elapsed.TotalSeconds:0.0} s");
    return 0;
}

/// <summary>
/// Corrections change both databases one after the other, as changes are not synchronised. If only one were
/// changed, they would differ for good (and rescaling is not repeatable), so nothing is changed unless both answer.
/// </summary>
static async Task<bool> BothReachableAsync()
{
    foreach (DatabaseProvider provider in new[] { DatabaseProvider.PostgreSql, DatabaseProvider.SqlServer })
    {
        await using TrafficCounterDbContext db = Open(provider);
        if (!await db.Database.CanConnectAsync())
        {
            Console.Error.WriteLine($"{provider} is not reachable; nothing was changed. Run again when both databases are reachable.");
            return false;
        }
    }

    return true;
}

static async Task<int> RescaleSpeedAsync(Dictionary<string, string> options)
{
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    DateTimeOffset from = DateTimeOffset.Parse(options["from"], invariant);
    DateTimeOffset to = DateTimeOffset.Parse(options["to"], invariant);
    float[] old = [.. options["old"].Split('/').Select(v => float.Parse(v, invariant))];
    float[] @new = [.. options["new"].Split('/').Select(v => float.Parse(v, invariant))];
    float farKerb = float.Parse(options["far-kerb"], invariant);
    float nearKerb = float.Parse(options["near-kerb"], invariant);
    bool oldLinearMeters = options.GetValueOrDefault("old-interpolation") == "linear-meters";
    bool dryRun = options.GetValueOrDefault("dry-run") is "true";
    string? machine = options.GetValueOrDefault("machine");

    // Meters per image width at a position along the counting line (the image width cancels out in the ratio).
    float Scale(float[] kerbs, float position, bool linearMeters)
    {
        float t = (position - farKerb) / (nearKerb - farKerb);
        return linearMeters
            ? kerbs[0] + (t * (kerbs[1] - kerbs[0]))
            : 1 / ((1 / kerbs[0]) + (t * ((1 / kerbs[1]) - (1 / kerbs[0]))));
    }

    if (!dryRun && !await BothReachableAsync())
    {
        return 1;
    }

    foreach (DatabaseProvider provider in new[] { DatabaseProvider.PostgreSql, DatabaseProvider.SqlServer })
    {
        await using TrafficCounterDbContext db = Open(provider);
        // Only the passages of one counting machine, e.g. not those copied in from a second camera by fill-degraded.
        List<int> periods = machine is null
            ? []
            : await db.OperatingPeriods.Where(o => o.MachineName == machine).Select(o => o.Id).ToListAsync();
        List<VehiclePassage> passages = await db.VehiclePassages
            .Where(p => p.Source == DataSource.Counter && p.SpeedKmh != null && p.PassedAt >= from && p.PassedAt < to)
            .Where(p => machine == null || periods.Contains(p.OperatingPeriodId))
            .ToListAsync();
        double before = 0, after = 0;
        foreach (VehiclePassage passage in passages)
        {
            float position = passage.LinePosition ?? (farKerb + nearKerb) / 2;
            float factor = Scale(@new, position, linearMeters: false) / Scale(old, position, oldLinearMeters);
            before += passage.SpeedKmh!.Value;
            passage.SpeedKmh *= factor;
            after += passage.SpeedKmh!.Value;
        }

        if (!dryRun)
        {
            await db.SaveChangesAsync();
        }

        Console.WriteLine(passages.Count == 0
            ? $"{provider}: no speeds in the period"
            : $"{provider}: {passages.Count} speeds {(dryRun ? "would be " : "")}rescaled, mean {before / passages.Count:0.0} -> {after / passages.Count:0.0} km/h");
    }

    return 0;
}

static async Task<int> MarkUnmeasuredAsync(Dictionary<string, string> options)
{
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    DateTimeOffset from = DateTimeOffset.Parse(options["from"], invariant);
    DateTimeOffset to = DateTimeOffset.Parse(options["to"], invariant);
    bool dryRun = options.GetValueOrDefault("dry-run") is "true";

    // Changes are not synchronised (only missing rows are copied), so both databases are updated.
    if (!dryRun && !await BothReachableAsync())
    {
        return 1;
    }

    foreach (DatabaseProvider provider in new[] { DatabaseProvider.PostgreSql, DatabaseProvider.SqlServer })
    {
        await using TrafficCounterDbContext db = Open(provider);
        IQueryable<MinuteStatistic> minutes = db.MinuteStatistics
            .Where(m => m.SourceState == TrafficCounter.Core.Operations.SourceState.Ok && m.MinuteStart >= from && m.MinuteStart < to);
        int count = dryRun
            ? await minutes.CountAsync()
            : await minutes.ExecuteUpdateAsync(s => s.SetProperty(m => m.SourceState, TrafficCounter.Core.Operations.SourceState.Unavailable));
        Console.WriteLine($"{provider}: {count} minutes {(dryRun ? "would be " : "")}marked as not measured ({from:yyyy-MM-dd HH:mm}–{to:HH:mm})");
    }

    return 0;
}

static async Task<int> CorrectPassagesAsync(Dictionary<string, string> options, VehicleClass? newClass)
{
    List<Guid> ids = [.. options["sync-ids"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Guid.Parse)];
    bool dryRun = options.GetValueOrDefault("dry-run") is "true";

    // Changes are not synchronised (only missing rows are copied), so both databases are corrected.
    if (!dryRun && !await BothReachableAsync())
    {
        return 1;
    }

    foreach (DatabaseProvider provider in new[] { DatabaseProvider.PostgreSql, DatabaseProvider.SqlServer })
    {
        await using TrafficCounterDbContext db = Open(provider);
        IQueryable<VehiclePassage> passages = db.VehiclePassages.Where(p => ids.Contains(p.SyncId));
        int count = dryRun
            ? await passages.CountAsync()
            : newClass is VehicleClass vehicleClass
                ? await passages.ExecuteUpdateAsync(s => s.SetProperty(p => p.VehicleClass, vehicleClass))
                : await passages.ExecuteDeleteAsync();
        string action = newClass is null ? "deleted" : $"set to {newClass}";
        Console.WriteLine($"{provider}: {count} of {ids.Count} passages {(dryRun ? "would be " : "")}{action}");
    }

    return 0;
}

static async Task<int> SetTrailersAsync(Dictionary<string, string> options)
{
    List<Guid> ids = [.. options["sync-ids"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Guid.Parse)];
    byte trailers = byte.Parse(options["trailers"], System.Globalization.CultureInfo.InvariantCulture);
    bool dryRun = options.GetValueOrDefault("dry-run") is "true";
    if (!dryRun && !await BothReachableAsync())
    {
        return 1;
    }

    foreach (DatabaseProvider provider in new[] { DatabaseProvider.PostgreSql, DatabaseProvider.SqlServer })
    {
        await using TrafficCounterDbContext db = Open(provider);
        IQueryable<VehiclePassage> passages = db.VehiclePassages.Where(p => ids.Contains(p.SyncId));
        int count = dryRun ? await passages.CountAsync() : await passages.ExecuteUpdateAsync(s => s.SetProperty(p => p.TrailerCount, trailers));
        Console.WriteLine($"{provider}: {count} of {ids.Count} passages {(dryRun ? "would be " : "")}set to {trailers} trailer(s)");
    }

    return 0;
}

static async Task<int> FillDegradedAsync(Dictionary<string, string> options)
{
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    DateTimeOffset from = DateTimeOffset.Parse(options["from"], invariant);
    DateTimeOffset to = DateTimeOffset.Parse(options["to"], invariant);
    string sourceConnection = options.GetValueOrDefault("source-db") ?? throw new ArgumentException("--source-db is required.");
    bool dryRun = options.GetValueOrDefault("dry-run") is "true";
    if (!dryRun && !await BothReachableAsync())
    {
        return 1;
    }

    foreach (DatabaseProvider provider in new[] { DatabaseProvider.PostgreSql, DatabaseProvider.SqlServer })
    {
        await using TrafficCounterDbContext source = TrafficCounterDatabase.Create(DatabaseProvider.SqlServer, sourceConnection);
        await using TrafficCounterDbContext target = Open(provider);
        GapFillResult result = await GapFill.FillAsync(
            source, target, from, to, dryRun, CancellationToken.None, includeMissing: options.GetValueOrDefault("include-missing") is "true");
        Console.WriteLine($"{provider}: {(dryRun ? "would fill " : "")}{result}");
    }

    return 0;
}

static async Task<int> RestoreFromLogAsync(Dictionary<string, string> options)
{
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    DateTimeOffset from = DateTimeOffset.Parse(options["from"], invariant);
    DateTimeOffset to = DateTimeOffset.Parse(options["to"], invariant);
    string machine = options.GetValueOrDefault("machine") ?? throw new ArgumentException("--machine is required.");
    bool dryRun = options.GetValueOrDefault("dry-run") is "true";
    (List<LoggedPassage> passages, List<LoggedMinute> minutes) = LogRestore.Parse(
        options["log"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).SelectMany(File.ReadLines));
    Console.WriteLine($"Log: {passages.Count} passages, {minutes.Count} minutes");
    (double? latitude, double? longitude) = SiteCoordinatesFromConfig();
    var lighting = new TrafficCounter.Core.Operations.LightingClassifier(latitude, longitude);
    if (!dryRun && !await BothReachableAsync())
    {
        return 1;
    }

    foreach (DatabaseProvider provider in new[] { DatabaseProvider.PostgreSql, DatabaseProvider.SqlServer })
    {
        await using TrafficCounterDbContext db = Open(provider);
        LogRestoreResult result = await LogRestore.RestoreAsync(db, passages, minutes, machine, from, to, lighting, dryRun, CancellationToken.None);
        Console.WriteLine($"{provider}: {(dryRun ? "would restore " : "")}{result}");
    }

    return 0;
}

static (double? Latitude, double? Longitude) SiteCoordinatesFromConfig()
{
    if (PrivateLocations.ConfigPath is not { } path || !File.Exists(path))
    {
        return (null, null);
    }

    using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
    return json.RootElement.TryGetProperty("TrafficCounter", out var root) && root.TryGetProperty("Site", out var site)
        && site.TryGetProperty("Latitude", out var latitude) && site.TryGetProperty("Longitude", out var longitude)
        ? (latitude.GetDouble(), longitude.GetDouble())
        : (null, null);
}

static async Task<int> AddAlongsideAsync(Dictionary<string, string> options)
{
    Guid id = Guid.Parse(options["sync-id"]);
    await using TrafficCounterDbContext db = Open(DatabaseProvider.PostgreSql);
    VehiclePassage original = await db.VehiclePassages.AsNoTracking().SingleAsync(p => p.SyncId == id);
    VehiclePassage copy = await db.VehiclePassages.AsNoTracking().SingleAsync(p => p.SyncId == id);
    copy.Id = 0;
    copy.SyncId = Guid.NewGuid();
    copy.PassedAt = original.PassedAt.AddMilliseconds(500); // side by side: practically the same moment
    copy.TrailerCount = 0; // the trailers, if any, belong to the original
    if (options.TryGetValue("class", out string? vehicleClass))
    {
        copy.VehicleClass = Enum.Parse<VehicleClass>(vehicleClass, ignoreCase: true);
    }

    db.VehiclePassages.Add(copy);
    await db.SaveChangesAsync();
    Console.WriteLine($"PostgreSql: added {copy.VehicleClass} at {copy.PassedAt:yyyy-MM-dd HH:mm:ss.fff} beside {original.VehicleClass}, sync id {copy.SyncId}");
    return 0;
}

static async Task<int> WeatherAsync(DatabaseProvider provider, string? station, string? from, string? to)
{
    if (string.IsNullOrWhiteSpace(station))
    {
        Console.Error.WriteLine("No DWD station: pass --station or set TrafficCounter:Weather:StationId in the site configuration.");
        return 2;
    }

    DateTimeOffset end = to is null ? DateTimeOffset.Now : new DateTimeOffset(DateTime.Parse(to, System.Globalization.CultureInfo.InvariantCulture).AddDays(1));
    DateTimeOffset start = from is null ? end.AddDays(-7) : new DateTimeOffset(DateTime.Parse(from, System.Globalization.CultureInfo.InvariantCulture));
    await using TrafficCounterDbContext db = Open(provider);
    await db.Database.MigrateAsync();
    using var http = new HttpClient { BaseAddress = new Uri(WeatherImport.DefaultBaseUrl), Timeout = TimeSpan.FromSeconds(60) };
    int changed = 0;
    // Bright Sky answers at most about ten days per request.
    for (DateTimeOffset chunk = start; chunk < end; chunk = chunk.AddDays(7))
    {
        DateTimeOffset chunkEnd = chunk.AddDays(7) < end ? chunk.AddDays(7) : end;
        changed += await WeatherImport.ImportAsync(db, http, station, chunk, chunkEnd);
    }

    Console.WriteLine($"{provider}: {changed} weather hours of station {station} inserted or updated ({start:yyyy-MM-dd}–{end:yyyy-MM-dd})");
    return 0;
}

static async Task<int> HolidaysAsync(Dictionary<string, string> options)
{
    string? subdivision = options.GetValueOrDefault("subdivision") ?? SiteSetting("Holidays", "Subdivision");
    if (string.IsNullOrWhiteSpace(subdivision))
    {
        Console.Error.WriteLine("No region: pass --subdivision or set TrafficCounter:Holidays:Subdivision in the site configuration.");
        return 2;
    }

    DatabaseProvider provider = options.TryGetValue("provider", out string? name) ? Enum.Parse<DatabaseProvider>(name, ignoreCase: true) : TrafficCounterDatabase.DefaultProvider;
    int year = DateTime.Today.Year;
    int fromYear = options.TryGetValue("from-year", out string? f) ? int.Parse(f, System.Globalization.CultureInfo.InvariantCulture) : year - 1;
    int toYear = options.TryGetValue("to-year", out string? t) ? int.Parse(t, System.Globalization.CultureInfo.InvariantCulture) : year + 1;
    await using TrafficCounterDbContext db = Open(provider);
    await db.Database.MigrateAsync();
    using var http = new HttpClient { BaseAddress = new Uri(HolidayImport.DefaultBaseUrl), Timeout = TimeSpan.FromSeconds(60) };
    int changed = await HolidayImport.ImportAsync(db, http, subdivision, new DateOnly(fromYear, 1, 1), new DateOnly(toYear, 12, 31));
    Console.WriteLine($"{provider}: {changed} holidays of {subdivision} inserted, changed or removed ({fromYear}–{toYear})");
    return 0;
}

async Task<int> ListNotesAsync(Dictionary<string, string> options)
{
    DateTime from = options.TryGetValue("from", out string? f) ? ParseDay(f) : DateTime.Today.AddDays(-30);
    DateTime to = options.TryGetValue("to", out string? t) ? ParseDay(t).AddDays(1) : DateTime.Today.AddDays(31);
    await using TrafficCounterDbContext db = Open(DatabaseProvider.PostgreSql);
    foreach (DayNote note in await DayNotes.ListAsync(db, new DateTimeOffset(from), new DateTimeOffset(to)))
    {
        Console.WriteLine($"{note.SyncId}  {note.Start.ToLocalTime():yyyy-MM-dd HH:mm} – {note.End.ToLocalTime():yyyy-MM-dd HH:mm}  {note.Text}");
    }

    return 0;
}

async Task<int> AddNoteAsync(Dictionary<string, string> options)
{
    DateTimeOffset start = ParseNoteTime(options["from"], endOfDay: false);
    DateTimeOffset end = ParseNoteTime(options["to"], endOfDay: true);
    await using TrafficCounterDbContext db = Open(DatabaseProvider.PostgreSql);
    DayNote note = await DayNotes.AddAsync(db, start, end, options["text"]);
    Console.WriteLine($"PostgreSql: note {note.SyncId} added ({start:yyyy-MM-dd HH:mm} – {end:yyyy-MM-dd HH:mm}); the next sync copies it on");
    return 0;
}

async Task<int> DeleteNoteAsync(Dictionary<string, string> options)
{
    await using TrafficCounterDbContext db = Open(DatabaseProvider.PostgreSql);
    bool deleted = await DayNotes.DeleteAsync(db, Guid.Parse(options["id"]));
    Console.WriteLine(deleted ? "PostgreSql: note deleted; the next sync passes it on" : "No such note");
    return deleted ? 0 : 1;
}

static DateTime ParseDay(string value) => DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture).Date;

// A day without a time: its start, or as end the midnight after it, so the end day is included.
static DateTimeOffset ParseNoteTime(string value, bool endOfDay)
{
    DateTime time = DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    bool dayOnly = value.Trim().Length <= 10;
    return new DateTimeOffset(dayOnly && endOfDay ? time.Date.AddDays(1) : time);
}

static string? SiteSetting(string section, string key)
{
    if (PrivateLocations.ConfigPath is not { } path || !File.Exists(path))
    {
        return null;
    }

    using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
    return json.RootElement.TryGetProperty("TrafficCounter", out var root)
        && root.TryGetProperty(section, out var part)
        && part.TryGetProperty(key, out var value) ? value.GetString() : null;
}

static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, string name)
{
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("name", name);
    return await command.ExecuteScalarAsync();
}

static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
}

static string Identifier(string name) =>
    name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')
        ? name
        : throw new ArgumentException($"Use lower-case letters, digits and '_' for PostgreSQL names ('{name}').");

static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
