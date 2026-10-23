using Microsoft.Extensions.Configuration;

namespace TrafficCounter.Service.Tests;

public sealed class ConfigurationSetupTests : IDisposable
{
    private const string Key = "TrafficCounter:Site:CameraName";
    private readonly string _directory = Directory.CreateTempSubdirectory("tc-config-").FullName;
    private readonly string _envPrefix = $"TCTEST{Guid.NewGuid():N}_";

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_envPrefix + "TrafficCounter__Site__CameraName", null);
        Directory.Delete(_directory, recursive: true);
    }

    private string Json(string name, string cameraName)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, $$"""{ "TrafficCounter": { "Site": { "CameraName": "{{cameraName}}" } } }""");
        return path;
    }

    /// <summary>
    /// Same source order as Host.CreateApplicationBuilder: DOTNET_ environment variables and command line for the
    /// host come first (this once put the private file before appsettings), then appsettings, environment, command line.
    /// </summary>
    private IConfigurationBuilder HostLikeBuilder(string[] args) => new ConfigurationBuilder()
        .AddEnvironmentVariables("DOTNET_")
        .AddCommandLine(args)
        .AddJsonFile(Json("appsettings.json", "from-appsettings"))
        .AddEnvironmentVariables(_envPrefix)
        .AddCommandLine(args);

    [Fact]
    public void Private_file_overrides_appsettings()
    {
        IConfigurationBuilder builder = HostLikeBuilder([]);
        ConfigurationSetup.AddPrivateConfiguration(builder, Json("site.json", "from-private"));

        Assert.Equal("from-private", builder.Build()[Key]);
    }

    [Fact]
    public void Command_line_overrides_the_private_file()
    {
        // This was broken once: the private file was appended after the command line.
        IConfigurationBuilder builder = HostLikeBuilder(CommandLine.Normalize(["--TrafficCounter:Site:CameraName=from-command-line"]));
        ConfigurationSetup.AddPrivateConfiguration(builder, Json("site.json", "from-private"));

        Assert.Equal("from-command-line", builder.Build()[Key]);
    }

    [Fact]
    public void Environment_variables_override_the_private_file()
    {
        Environment.SetEnvironmentVariable(_envPrefix + "TrafficCounter__Site__CameraName", "from-environment");
        IConfigurationBuilder builder = HostLikeBuilder([]);
        ConfigurationSetup.AddPrivateConfiguration(builder, Json("site.json", "from-private"));

        Assert.Equal("from-environment", builder.Build()[Key]);
    }

    [Fact]
    public void Missing_private_file_is_an_error() =>
        Assert.Throws<FileNotFoundException>(() => ConfigurationSetup.AddPrivateConfiguration(HostLikeBuilder([]), Path.Combine(_directory, "missing.json")));

    [Fact]
    public void Convenience_switches_are_translated()
    {
        string[] normalized = CommandLine.Normalize(["--preview", "--file", @"D:\videos\a.mp4", "--other", "x"]);

        Assert.Equal(
            [
                "--TrafficCounter:Preview=true",
                "--TrafficCounter:Source:Type=File",
                @"--TrafficCounter:Source:File:Path=D:\videos\a.mp4",
                "--other",
                "x",
            ],
            normalized);
    }
}
