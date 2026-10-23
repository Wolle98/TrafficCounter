using Microsoft.Extensions.Logging.Abstractions;

namespace TrafficCounter.Service.Tests;

public class NetworkProbeTests
{
    [Fact]
    public async Task A_minute_sums_up_the_pings_and_starts_again()
    {
        await using var probe = new NetworkProbe(new NetworkProbeOptions { Host = "127.0.0.1", IntervalSeconds = 0.5 }, NullLogger.Instance);
        probe.Start();
        await Task.Delay(TimeSpan.FromSeconds(1.8), TestContext.Current.CancellationToken);

        NetworkMinute? minute = probe.TakeMinute();
        Assert.NotNull(minute);
        Assert.Equal("127.0.0.1", minute.Host);
        Assert.True(minute.Sent >= 2, $"only {minute.Sent} pings sent");
        Assert.Equal(0, minute.Lost);
        Assert.NotNull(minute.Median);
        Assert.True(minute.Median <= minute.P90 && minute.P90 <= minute.Max);

        NetworkMinute? next = probe.TakeMinute();
        Assert.True(next is null || next.Sent <= 1, "the counts were not reset");
    }
}
