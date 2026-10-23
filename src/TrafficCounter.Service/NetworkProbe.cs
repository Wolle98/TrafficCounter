using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TrafficCounter.Service;

/// <summary>Settings of <see cref="NetworkProbe"/>.</summary>
public sealed class NetworkProbeOptions
{
    /// <summary>Measure the network round trip while counting. On by default for IP cameras.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Host to ping; empty: the default gateway (router) of the machine.</summary>
    public string? Host { get; set; }

    public double IntervalSeconds { get; set; } = 2;

    public int TimeoutMilliseconds { get; set; } = 2000;
}

/// <summary>Round trips of one minute; times in milliseconds, null when no ping was answered.</summary>
public sealed record NetworkMinute(string Host, int Sent, int Lost, long? Median, long? P90, long? Max);

/// <summary>
/// Pings the router (or a configured host) every few seconds while counting and sums up each minute, so that a
/// stream disturbance can be told apart: a slow or lossy network round trip points to the WLAN of the counting
/// machine, a normal one to the camera or the machine itself. Only measures, never changes anything.
/// </summary>
internal sealed class NetworkProbe : IAsyncDisposable
{
    private readonly NetworkProbeOptions _options;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _lock = new();
    private readonly List<long> _times = [];
    private int _sent;
    private int _lost;
    private string? _host;
    private Task? _loop;

    public NetworkProbe(NetworkProbeOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public void Start() => _loop = Task.Run(() => RunAsync(_stop.Token));

    /// <summary>Summary since the last call, or null if nothing was sent (no host found).</summary>
    public NetworkMinute? TakeMinute()
    {
        lock (_lock)
        {
            if (_sent == 0 || _host is null)
            {
                return null;
            }

            _times.Sort();
            var minute = new NetworkMinute(
                _host,
                _sent,
                _lost,
                _times.Count > 0 ? _times[_times.Count / 2] : null,
                _times.Count > 0 ? _times[Math.Min(_times.Count - 1, (int)(_times.Count * 0.9))] : null,
                _times.Count > 0 ? _times[^1] : null);
            _times.Clear();
            _sent = 0;
            _lost = 0;
            return minute;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var ping = new Ping();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(0.5, _options.IntervalSeconds)));
        DateTimeOffset resolveAt = DateTimeOffset.MinValue;
        string? host = null;
        try
        {
            do
            {
                if (DateTimeOffset.UtcNow >= resolveAt)
                {
                    // Re-resolved every 10 minutes: after a WLAN reconnect the gateway may change.
                    string? resolved = string.IsNullOrWhiteSpace(_options.Host) ? DefaultGateway() : _options.Host;
                    if (resolved != host)
                    {
                        _logger.LogInformation("Network probe pings {Host} every {Interval} s", resolved ?? "(no gateway found)", _options.IntervalSeconds);
                    }

                    host = resolved;
                    resolveAt = DateTimeOffset.UtcNow.AddMinutes(10);
                }

                if (host is null)
                {
                    continue;
                }

                long? time = null;
                try
                {
                    PingReply reply = await ping.SendPingAsync(host, _options.TimeoutMilliseconds).ConfigureAwait(false);
                    if (reply.Status == IPStatus.Success)
                    {
                        time = reply.RoundtripTime;
                    }
                }
                catch (PingException)
                {
                    // Counted as lost, e.g. while the WLAN reconnects.
                }

                lock (_lock)
                {
                    _host = host;
                    _sent++;
                    if (time is long t)
                    {
                        _times.Add(t);
                    }
                    else
                    {
                        _lost++;
                    }
                }
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string? DefaultGateway() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))
            ?.ToString();

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
        }

        _stop.Dispose();
    }
}
