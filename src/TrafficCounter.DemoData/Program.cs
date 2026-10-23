using System.Diagnostics;
using System.Globalization;
using TrafficCounter.Core;
using TrafficCounter.Data;
using TrafficCounter.DemoData;

Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
for (int i = 0; i + 1 < args.Length; i += 2)
{
    options[args[i].TrimStart('-')] = args[i + 1];
}

if (args is ["-h" or "--help", ..])
{
    Console.WriteLine(
        """
        TrafficCounter demo data – drops and recreates a demo database with synthetic traffic.

        Usage:
          TrafficCounter.DemoData [--connection <connection string>] [--days 21] [--first-day yyyy-MM-dd]
                                  [--seed 42] [--direction-a <name>] [--direction-b <name>]

        Defaults: connection = Server=localhost;Database=TrafficCounter_Demo;Integrated Security=true;
        TrustServerCertificate=true, first day = today minus days. The database name must contain "Demo".
        """);
    return 0;
}

string connection = options.GetValueOrDefault("connection")
    ?? "Server=localhost;Database=TrafficCounter_Demo;Integrated Security=true;TrustServerCertificate=true";
int days = int.Parse(options.GetValueOrDefault("days", "21"), CultureInfo.InvariantCulture);
DateOnly firstDay = options.TryGetValue("first-day", out string? first)
    ? DateOnly.ParseExact(first, "yyyy-MM-dd", CultureInfo.InvariantCulture)
    : DateOnly.FromDateTime(DateTime.Today).AddDays(-days);

DemoDataWriter.EnsureDemoDatabase(connection);

var stopwatch = Stopwatch.StartNew();
DemoDataSet data = new DemoDataGenerator(new DemoDataOptions
{
    FirstDay = firstDay,
    Days = days,
    Seed = int.Parse(options.GetValueOrDefault("seed", "42"), CultureInfo.InvariantCulture),
}).Generate();
Console.WriteLine($"Generated {data.Passages.Count} passages, {data.Minutes.Count} minutes, {data.Periods.Count} periods, "
    + $"{data.Events.Count} events for {firstDay:yyyy-MM-dd} + {days} days ({stopwatch.Elapsed.TotalSeconds:0.0} s).");

PrintSummary(data);

await DemoDataWriter.WriteAsync(
    connection, data, "Demo",
    options.GetValueOrDefault("direction-a", "Richtung A"), options.GetValueOrDefault("direction-b", "Richtung B"),
    CancellationToken.None);
Console.WriteLine($"Written to the demo database in {stopwatch.Elapsed.TotalSeconds:0.0} s.");
return 0;

static void PrintSummary(DemoDataSet data)
{
    int total = data.Passages.Count;
    Console.WriteLine("Classes: " + string.Join(", ", data.Passages
        .GroupBy(p => p.VehicleClass!.Value)
        .OrderBy(g => g.Key)
        .Select(g => $"{g.Key} {100.0 * g.Count() / total:0.0} %")));

    List<VehiclePassage> motor = [.. data.Passages.Where(p => SpeedModel.IsMotorVehicle(p.VehicleClass!.Value))];
    Console.WriteLine("Motor vehicle speeds: " + string.Join(", ", SpeedModel.MotorBands.Select(b =>
        $"{b.From}-{b.To} km/h {100.0 * motor.Count(p => p.SpeedKmh >= b.From && p.SpeedKmh < b.To) / motor.Count:0.0} %")));
    Console.WriteLine($"Direction A: {100.0 * data.Passages.Count(p => p.Direction == TravelDirection.A) / total:0.0} %");
}
