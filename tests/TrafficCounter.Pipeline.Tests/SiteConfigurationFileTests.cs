using System.Text.Json.Nodes;

namespace TrafficCounter.Pipeline.Tests;

public sealed class SiteConfigurationFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tc-site-").FullName;

    private string FilePath => Path.Combine(_directory, "config", "site.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Geometry_is_written_and_read_back()
    {
        var site = new SiteOptions
        {
            BandTop = 0.32f,
            BandBottom = 0.78f,
            LineStart = new RelativePoint { X = 0.5f, Y = 0.404f },
            LineEnd = new RelativePoint { X = 0.5f, Y = 0.773f },
            FarKerb = 0.18f,
            NearKerb = 0.843f,
            StaticZones = [new RelativeRect { X = 0.38f, Y = 0.08f, Width = 0.27f, Height = 0.14f }],
            IgnoreZones = [new RelativeRect { X = 0.55f, Y = 0.3f, Width = 0.2f, Height = 0.1f }],
        };

        SiteConfigurationFile.WriteGeometry(FilePath, site);
        SiteOptions read = SiteConfigurationFile.Read(FilePath);

        Assert.Equal((0.32f, 0.78f), (read.BandTop, read.BandBottom));
        Assert.Equal((0.5f, 0.773f), (read.LineEnd.X, read.LineEnd.Y));
        Assert.Equal((0.18f, 0.843f), (read.FarKerb, read.NearKerb));
        Assert.Equal(0.27f, Assert.Single(read.StaticZones).Width);
        Assert.Equal(0.55f, Assert.Single(read.IgnoreZones).X);
    }

    [Fact]
    public void Other_settings_in_the_file_are_kept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """
            {
              "TrafficCounter": {
                "Site": { "CameraName": "garden", "DirectionAName": "north", "BandTop": 0.1 },
                "Source": { "Usb": { "Exposure": -7 } }
              },
              "ConnectionStrings": { "TrafficCounter": "Server=nas" }
            }
            """);

        SiteConfigurationFile.WriteGeometry(FilePath, new SiteOptions { BandTop = 0.3f, BandBottom = 0.8f });

        JsonNode root = JsonNode.Parse(File.ReadAllText(FilePath))!;
        Assert.Equal("garden", (string?)root["TrafficCounter"]!["Site"]!["CameraName"]);
        Assert.Equal("north", (string?)root["TrafficCounter"]!["Site"]!["DirectionAName"]);
        Assert.Equal(0.3f, (float)root["TrafficCounter"]!["Site"]!["BandTop"]!);
        Assert.Equal(-7, (int)root["TrafficCounter"]!["Source"]!["Usb"]!["Exposure"]!);
        Assert.Equal("Server=nas", (string?)root["ConnectionStrings"]!["TrafficCounter"]);
    }

    [Fact]
    public void Missing_file_gives_defaults() =>
        Assert.Equal(1f, SiteConfigurationFile.Read(FilePath).BandBottom);
}
