namespace TrafficCounter.Core;

/// <summary>
/// Locates the private repository and the site configuration (ADR-0002) via environment variables. A variable
/// missing from the process environment is also looked up in the stored user variables: processes started from
/// a long-running parent (IDE, terminal of an app) keep the environment from before a <c>setx</c>.
/// </summary>
public static class PrivateLocations
{
    public const string ConfigVariable = "TRAFFICCOUNTER_CONFIG";
    public const string PrivateRootVariable = "TRAFFICCOUNTER_PRIVATE";

    /// <summary>Root of the private repository, or null.</summary>
    public static string? PrivateRoot => Get(PrivateRootVariable);

    /// <summary>TRAFFICCOUNTER_CONFIG, else <c>config\site.json</c> in the private repository, or null.</summary>
    public static string? ConfigPath =>
        Get(ConfigVariable) ?? (PrivateRoot is { } root ? Path.Combine(root, "config", "site.json") : null);

    /// <summary>Process variable first, then the stored user variable (Windows only).</summary>
    public static string? Get(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value) && OperatingSystem.IsWindows())
        {
            value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
