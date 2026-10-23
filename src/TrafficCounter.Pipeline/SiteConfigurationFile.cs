using System.Text.Json;
using System.Text.Json.Nodes;

namespace TrafficCounter.Pipeline;

/// <summary>
/// Reads and updates the "TrafficCounter:Site" section of the private site configuration file, leaving all other
/// settings in the file untouched.
/// </summary>
public static class SiteConfigurationFile
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static SiteOptions Read(string path)
    {
        if (!File.Exists(path))
        {
            return new SiteOptions();
        }

        JsonNode? site = JsonNode.Parse(File.ReadAllText(path))?["TrafficCounter"]?["Site"];
        return site?.Deserialize<SiteOptions>() ?? new SiteOptions();
    }

    /// <summary>Writes the calibrated geometry. Names and other site values already in the file are kept.</summary>
    public static void WriteGeometry(string path, SiteOptions site)
    {
        JsonObject root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? [] : [];
        JsonObject section = GetOrAdd(GetOrAdd(root, "TrafficCounter"), "Site");

        section["BandTop"] = Round(site.BandTop);
        section["BandBottom"] = Round(site.BandBottom);
        section["BandLeft"] = Round(site.BandLeft);
        section["BandRight"] = Round(site.BandRight);
        section["LineStart"] = new JsonObject { ["X"] = Round(site.LineStart.X), ["Y"] = Round(site.LineStart.Y) };
        section["LineEnd"] = new JsonObject { ["X"] = Round(site.LineEnd.X), ["Y"] = Round(site.LineEnd.Y) };
        section["FarKerb"] = site.FarKerb is float far ? Round(far) : null;
        section["NearKerb"] = site.NearKerb is float near ? Round(near) : null;
        section["StaticZones"] = Zones(site.StaticZones);
        section["IgnoreZones"] = Zones(site.IgnoreZones);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, root.ToJsonString(Options));
    }

    private static JsonArray Zones(IEnumerable<RelativeRect> zones) =>
        new([.. zones.Select(z => (JsonNode)new JsonObject
        {
            ["X"] = Round(z.X),
            ["Y"] = Round(z.Y),
            ["Width"] = Round(z.Width),
            ["Height"] = Round(z.Height),
        })]);

    private static JsonObject GetOrAdd(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[name] = created;
        return created;
    }

    private static float Round(float value) => MathF.Round(value, 4);
}
