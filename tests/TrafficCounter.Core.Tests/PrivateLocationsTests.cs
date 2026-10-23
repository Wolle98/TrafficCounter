namespace TrafficCounter.Core.Tests;

public class PrivateLocationsTests
{
    [Fact]
    public void Process_variable_is_used()
    {
        const string name = "TRAFFICCOUNTER_TEST_PRIVATE_LOCATION";
        Environment.SetEnvironmentVariable(name, @"X:\somewhere");
        try
        {
            Assert.Equal(@"X:\somewhere", PrivateLocations.Get(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void Missing_variable_gives_null() =>
        Assert.Null(PrivateLocations.Get("TRAFFICCOUNTER_TEST_NOT_SET_" + Guid.NewGuid().ToString("N")));
}
