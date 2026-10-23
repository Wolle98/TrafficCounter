using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace TrafficCounter.Service;

/// <summary>Directory for logs and the database fallback file.</summary>
internal sealed record DataPaths(string Root)
{
    public string DefaultFallbackFile => Path.Combine(Root, "fallback", "pending-records.jsonl");
}

internal static class CommandLine
{
    /// <summary>
    /// Translates the convenience switches into configuration keys:
    /// <c>--preview</c> → debug window, <c>--file &lt;video&gt;</c> → replay a recording instead of the camera.
    /// </summary>
    public static string[] Normalize(string[] args)
    {
        List<string> result = [];
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--preview":
                    result.Add($"--{ServiceOptions.SectionName}:Preview=true");
                    break;
                case "--file" when i + 1 < args.Length:
                    result.Add($"--{ServiceOptions.SectionName}:Source:Type=File");
                    result.Add($"--{ServiceOptions.SectionName}:Source:File:Path={args[++i]}");
                    break;
                default:
                    result.Add(args[i]);
                    break;
            }
        }

        return [.. result];
    }
}

internal static class ConfigurationSetup
{
    public const string PrivateConfigVariable = Core.PrivateLocations.ConfigVariable;

    /// <summary>
    /// Inserts the private site configuration (TRAFFICCOUNTER_CONFIG, ADR-0002) and the user secrets between the
    /// appsettings files and the environment variables. Resulting precedence, lowest first:
    /// appsettings → private site file → user secrets → environment variables → command line.
    /// </summary>
    public static void AddPrivateConfiguration(IConfigurationBuilder builder, string? privateConfigPath)
    {
        List<IConfigurationSource> additions = [];
        if (!string.IsNullOrWhiteSpace(privateConfigPath))
        {
            if (!File.Exists(privateConfigPath))
            {
                throw new FileNotFoundException($"{PrivateConfigVariable} points to '{privateConfigPath}', which does not exist.", privateConfigPath);
            }

            var site = new JsonConfigurationSource { Path = Path.GetFullPath(privateConfigPath), Optional = false, ReloadOnChange = false };
            site.ResolveFileProvider();
            additions.Add(site);
        }

        additions.AddRange(new ConfigurationBuilder().AddUserSecrets(typeof(ConfigurationSetup).Assembly, optional: true).Sources);

        // Remove user secrets the host may have added itself (Development environment) to avoid duplicates.
        foreach (IConfigurationSource existing in builder.Sources.Where(IsUserSecrets).ToList())
        {
            builder.Sources.Remove(existing);
        }

        // The host adds DOTNET_ environment variables before appsettings.json, so insert after the last JSON file,
        // not before the first environment source.
        List<IConfigurationSource> sources = [.. builder.Sources];
        int lastJson = sources.FindLastIndex(s => s is JsonConfigurationSource);
        int index = sources.FindIndex(lastJson + 1, s => s is EnvironmentVariablesConfigurationSource or CommandLineConfigurationSource);
        index = index < 0 ? builder.Sources.Count : index;
        foreach (IConfigurationSource source in additions)
        {
            builder.Sources.Insert(index++, source);
        }
    }

    private static bool IsUserSecrets(IConfigurationSource source) =>
        source is JsonConfigurationSource json && json.Path?.EndsWith("secrets.json", StringComparison.OrdinalIgnoreCase) == true;

    public static string DataDirectory(LoggingOptions logging)
    {
        if (!string.IsNullOrWhiteSpace(logging.Directory))
        {
            return logging.Directory;
        }

        return WindowsServiceHelpers.IsWindowsService()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TrafficCounter")
            : AppContext.BaseDirectory;
    }

    public static string AppVersion =>
        typeof(ConfigurationSetup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>Short hash of the settings that influence counting, stored per operating period (ADR-0010).</summary>
    public static string ConfigHash(ServiceOptions options)
    {
        string json = JsonSerializer.Serialize(new { options.Site, options.Tracker, options.Detector });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16];
    }

    /// <summary>Resolves a relative model path against the executable's directory (services start in System32).</summary>
    public static string ResolvePath(string path) => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
