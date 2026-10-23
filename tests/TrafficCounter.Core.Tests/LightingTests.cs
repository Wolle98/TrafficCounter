using TrafficCounter.Core.Operations;

namespace TrafficCounter.Core.Tests;

public class LightingTests
{
    // Berlin (public reference location for the test, not the site).
    private const double Latitude = 52.52;
    private const double Longitude = 13.40;

    [Theory]
    [InlineData("2026-06-21T11:08:00Z", 60.9)]   // solar noon at summer solstice: 90 - 52.52 + 23.44
    [InlineData("2026-12-21T11:03:00Z", 14.0)]   // solar noon at winter solstice: 90 - 52.52 - 23.44
    [InlineData("2026-06-21T23:08:00Z", -14.0)]  // solar midnight in summer
    [InlineData("2026-03-20T11:10:00Z", 37.4)]   // equinox: 90 - 52.52 (plus a small offset of the date)
    public void Elevation_matches_reference_values(string utc, double expected) =>
        Assert.Equal(expected, SunPosition.ElevationDegrees(DateTimeOffset.Parse(utc), Latitude, Longitude), 0.6);

    [Fact]
    public void Sunset_in_Berlin_on_midsummer_is_around_19_33_utc()
    {
        // Geometric sunset (elevation 0°) is a few minutes before the published time, which includes refraction.
        Assert.True(SunPosition.ElevationDegrees(DateTimeOffset.Parse("2026-06-21T19:20:00Z"), Latitude, Longitude) > 0);
        Assert.True(SunPosition.ElevationDegrees(DateTimeOffset.Parse("2026-06-21T19:40:00Z"), Latitude, Longitude) < 0);
    }

    [Theory]
    [InlineData("2026-09-28T12:00:00+02:00", LightingCondition.Day)]
    [InlineData("2026-09-28T19:10:00+02:00", LightingCondition.Dusk)]  // Berlin: sunset ~18:55, civil dusk ends ~19:29 CEST
    [InlineData("2026-09-28T23:00:00+02:00", LightingCondition.Night)]
    public void Lighting_follows_the_sun(string time, LightingCondition expected) =>
        Assert.Equal(expected, new LightingClassifier(Latitude, Longitude).Classify(DateTimeOffset.Parse(time)));

    [Fact]
    public void Without_coordinates_lighting_is_unknown() =>
        Assert.Equal(LightingCondition.Unknown, new LightingClassifier(null, null).Classify(DateTimeOffset.Now));
}
