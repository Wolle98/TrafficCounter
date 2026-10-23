using System.Globalization;
using TrafficCounter.Vision;

namespace TrafficCounter.CameraProbe;

/// <summary>
/// Watches a camera stream for hours and writes one CSV line per minute: frames received, frames dropped by the
/// reader, reconnects and the longest gap between two frames. Run on a second, wired machine at the same time as the
/// counting service, it tells whether missing frames are lost in the camera or on the way to the counting machine.
/// </summary>
internal static class MonitorCommand
{
    public static async Task<int> RunAsync(IFrameSource source, string description, Arguments arguments)
    {
        double minutes = double.Parse(arguments.Get("minutes") ?? "0", CultureInfo.InvariantCulture); // 0: until Ctrl+C
        string csv = arguments.Get("out") ?? $"stream-monitor-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        if (minutes > 0)
        {
            stop.CancelAfter(TimeSpan.FromMinutes(minutes));
        }

        bool newFile = !File.Exists(csv);
        await using var writer = new StreamWriter(csv, append: true) { AutoFlush = true };
        if (newFile)
        {
            await writer.WriteLineAsync("minute;captured;dropped;reconnects;max_gap_ms;received_by_reader");
        }

        Console.WriteLine($"Monitoring {description}, one line per minute into {Path.GetFullPath(csv)}{(minutes > 0 ? $" for {minutes} min" : " until Ctrl+C")}.");
        await using (source)
        {
            source.Start();
            FrameSourceStatistics last = source.Statistics;
            DateTime minute = Truncate(DateTime.Now);
            DateTimeOffset? lastFrameAt = null;
            double maxGap = 0;
            int received = 0;
            while (!stop.IsCancellationRequested)
            {
                if (source.TryRead(TimeSpan.FromSeconds(1), out Frame? frame))
                {
                    using (frame)
                    {
                        if (lastFrameAt is DateTimeOffset previous)
                        {
                            maxGap = Math.Max(maxGap, (frame!.Timestamp - previous).TotalMilliseconds);
                        }

                        lastFrameAt = frame!.Timestamp;
                        received++;
                    }
                }

                DateTime now = DateTime.Now;
                if (Truncate(now) > minute)
                {
                    FrameSourceStatistics stats = source.Statistics;
                    // A minute without any frame counts its full length as gap.
                    if (lastFrameAt is DateTimeOffset seen && (now - seen.LocalDateTime).TotalMilliseconds > maxGap)
                    {
                        maxGap = Math.Max(maxGap, Math.Min(60_000, (now - seen.LocalDateTime).TotalMilliseconds));
                    }

                    string line = string.Create(CultureInfo.InvariantCulture,
                        $"{minute:yyyy-MM-dd HH:mm};{stats.Captured - last.Captured};{stats.Dropped - last.Dropped};{stats.Reconnects - last.Reconnects};{maxGap:0};{received}");
                    await writer.WriteLineAsync(line);
                    Console.WriteLine(line);
                    last = stats;
                    minute = Truncate(now);
                    maxGap = 0;
                    received = 0;
                }
            }
        }

        return 0;
    }

    private static DateTime Truncate(DateTime time) => new(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, time.Kind);
}
