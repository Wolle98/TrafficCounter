using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using TrafficCounter.Counting;
using TrafficCounter.Pipeline;

namespace TrafficCounter.Service.Tests;

/// <summary>The camera model (ADR-0056) reaches the service through the configuration binder, the regression through JSON.</summary>
public class CameraModelBindingTests
{
    private const string Site = """
        { "TrafficCounter": { "Site": { "Camera": {
            "Focal": 1184.761, "PrincipalX": 888.404, "PrincipalY": 612.437, "K1": -0.333155,
            "Rotation": [1.23813206, 1.87393882, -1.32005522], "Center": [11.4095, 2.651, 4.2822],
            "Ground": [-0.254067, -0.004786, -0.016314], "RoadOrigin": [-1.7948, -2.2601],
            "RoadAxis": [0.33918817, -0.94071855], "AcrossAxis": [0.94071855, 0.33918817] } } } }
        """;

    [Fact]
    public void The_binder_gives_the_same_model_as_the_json_file()
    {
        // The binder appends configured array items to the defaults; with non-empty defaults the service once got
        // a rotation of zero and measured 0 km/h.
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(Site)))
            .Build();
        CameraModelOptions bound = configuration.GetSection(ServiceOptions.SectionName).Get<ServiceOptions>()!.Site.Camera!;
        CameraModelOptions read = JsonNode.Parse(Site)!["TrafficCounter"]!["Site"]!["Camera"].Deserialize<CameraModelOptions>()!;

        CameraModel fromBinder = bound.Model.ForFrame(1920, 1080);
        CameraModel fromJson = read.Model.ForFrame(1920, 1080);

        Assert.Equal(fromJson.Rotation, fromBinder.Rotation);
        Assert.Equal(fromJson.Center, fromBinder.Center);
        Assert.Equal(fromJson.ToRoad(900, 700), fromBinder.ToRoad(900, 700));
    }

    [Fact]
    public void A_camera_model_with_missing_values_is_refused()
    {
        var options = new CameraModelOptions { Focal = 1000, Rotation = [1, 2], Center = [0, 0, 5] };

        Assert.Throws<InvalidOperationException>(() => options.Model);
    }
}
