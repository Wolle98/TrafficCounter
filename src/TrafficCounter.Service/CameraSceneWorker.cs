using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using TrafficCounter.Core.Operations;

namespace TrafficCounter.Service;

/// <summary>Settings of <see cref="CameraSceneWorker"/> (ADR-0042).</summary>
public sealed class CameraScenesOptions
{
    /// <summary>Check the IP camera's scene schedule once a day against sunrise and sunset.</summary>
    public bool Enabled { get; set; }

    /// <summary>Write the new times into the camera; false: only log what would be set.</summary>
    public bool Apply { get; set; }

    /// <summary>Local time of the daily check (and once at start).</summary>
    public TimeOnly CheckAt { get; set; } = new(3, 5);

    public SceneScheduleSettings Times { get; set; } = new();
}

/// <summary>
/// Keeps the day/night scene switch of a Hikvision-type IP camera (Annke NC400) in step with sunrise and sunset
/// (ADR-0042). The camera switches scenes itself by a schedule with one entry per month (ISAPI
/// <c>displayParamSwitch</c>, mode "month": night, day, backlight, night). Once a day this worker computes the day and
/// night start for today and adjusts the entry of the current month: the end of the morning night range and the start
/// of the evening night range; a range in between (backlight) is only moved if it would collide. Nothing else of the
/// camera is touched. With <see cref="CameraScenesOptions.Apply"/> false it only logs.
/// </summary>
internal sealed class CameraSceneWorker(
    IOptions<ServiceOptions> options, ILogger<CameraSceneWorker> logger, DataPaths paths, TimeProvider time) : BackgroundService
{
    private const string SchedulePath = "/ISAPI/Image/channels/1/displayParamSwitch";
    private readonly ServiceOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CameraScenesOptions scenes = _options.CameraScenes;
        if (!scenes.Enabled)
        {
            return;
        }

        if (_options.Source.Type != SourceType.Rtsp || _options.Site.Latitude is not double lat || _options.Site.Longitude is not double lon
            || !Uri.TryCreate(_options.Source.Rtsp.Url, UriKind.Absolute, out Uri? rtsp) || string.IsNullOrEmpty(_options.Source.Rtsp.UserName))
        {
            logger.LogWarning("Camera scenes: needs an RTSP camera with user name and the site's latitude/longitude; switched off");
            return;
        }

        var baseUri = new Uri($"http://{rtsp.Host}");
        using var handler = new HttpClientHandler
        {
            Credentials = new CredentialCache { { baseUri, "Digest", new NetworkCredential(_options.Source.Rtsp.UserName, _options.Source.Rtsp.Password) } },
        };
        using var http = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(20) };
        logger.LogInformation("Camera scenes: daily check at {CheckAt:HH\\:mm}, {Mode}", scenes.CheckAt, scenes.Apply ? "applying changes" : "report only");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(http, lat, lon, scenes, stoppingToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException && !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Camera scenes: check failed, next try at the next check time");
            }

            DateTimeOffset now = time.GetLocalNow();
            DateTimeOffset next = new DateTimeOffset(now.Date, now.Offset).Add(scenes.CheckAt.ToTimeSpan());
            if (next <= now)
            {
                next = next.AddDays(1);
            }

            await Task.Delay(next - now, time, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task CheckAsync(HttpClient http, double lat, double lon, CameraScenesOptions scenes, CancellationToken ct)
    {
        DateTimeOffset now = time.GetLocalNow();
        SceneTimes times = SceneSchedule.For(now, lat, lon, scenes.Times);
        string xml = await http.GetStringAsync(SchedulePath, ct).ConfigureAwait(false);
        XDocument doc = XDocument.Parse(xml);
        DisplayParamSwitchChange change = DisplayParamSwitch.Adjust(doc, now.Month, times.DayStart, times.NightStart);
        logger.LogInformation(
            "Camera scenes {Month}: sunrise {Sunrise:HH\\:mm}, sunset {Sunset:HH\\:mm}; {Result}",
            now.Month, times.Sunrise, times.Sunset, change.Description);
        if (!change.Changed || !scenes.Apply)
        {
            return;
        }

        string backup = Path.Combine(paths.Root, $"camera-scenes-{now:yyyyMMdd-HHmmss}.xml");
        await File.WriteAllTextAsync(backup, xml, ct).ConfigureAwait(false);
        using var content = new StringContent(doc.Declaration + Environment.NewLine + doc.Root, Encoding.UTF8, "application/xml");
        using HttpResponseMessage response = await http.PutAsync(SchedulePath, content, ct).ConfigureAwait(false);
        string answer = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Camera scenes: camera refused the new schedule ({Status}): {Answer}", (int)response.StatusCode, answer);
            return;
        }

        XDocument stored = XDocument.Parse(await http.GetStringAsync(SchedulePath, ct).ConfigureAwait(false));
        DisplayParamSwitchChange check = DisplayParamSwitch.Adjust(stored, now.Month, times.DayStart, times.NightStart);
        logger.Log(check.Changed ? LogLevel.Warning : LogLevel.Information,
            "Camera scenes {Month}: {Result} (previous schedule saved in {Backup})",
            now.Month, check.Changed ? "the camera did not keep the new times" : "new times stored in the camera", backup);
    }
}

/// <summary>Result of <see cref="DisplayParamSwitch.Adjust"/>.</summary>
internal sealed record DisplayParamSwitchChange(bool Changed, string Description);

/// <summary>Edits the month entry of the camera's scene schedule XML (ISAPI <c>DisplayParamSwitch</c>).</summary>
internal static class DisplayParamSwitch
{
    private const int Grid = 30;

    /// <summary>
    /// Sets the end of the first range (night until day start) and the start of the last range (night from night
    /// start) of the month's entry; ranges in between are kept, a boundary between them is only moved to stay inside
    /// the day. Changes <paramref name="doc"/> in place.
    /// </summary>
    public static DisplayParamSwitchChange Adjust(XDocument doc, int month, TimeOnly dayStart, TimeOnly nightStart)
    {
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        if ((string?)doc.Root?.Element(ns + "mode") != "month")
        {
            return new(false, "schedule not in month mode, left alone");
        }

        XElement? period = doc.Root!.Descendants(ns + "Period").FirstOrDefault(p => (string?)p.Element(ns + "id") == month.ToString(CultureInfo.InvariantCulture));
        List<XElement> ranges = period?.Descendants(ns + "TimeRange").ToList() ?? [];
        if (ranges.Count < 2)
        {
            return new(false, $"month {month} has {ranges.Count} time ranges, left alone");
        }

        int[] bounds = [.. ranges.Skip(1).Select(r => Minutes((string)r.Element(ns + "beginTime")!))];
        int day = dayStart.Hour * 60 + dayStart.Minute;
        int night = nightStart.Hour * 60 + nightStart.Minute;
        int[] wanted = (int[])bounds.Clone();
        wanted[0] = day;
        wanted[^1] = night;
        for (int i = 1; i < wanted.Length - 1; i++)
        {
            wanted[i] = Math.Clamp(wanted[i], day + (i * Grid), night - ((wanted.Length - 1 - i) * Grid));
        }

        if (wanted.Zip(wanted.Skip(1)).Any(p => p.First >= p.Second))
        {
            return new(false, $"day {Format(day)}–{Format(night)} too short for {ranges.Count} ranges, left alone");
        }

        string before = string.Join(", ", bounds.Select(Format));
        string after = string.Join(", ", wanted.Select(Format));
        if (bounds.SequenceEqual(wanted))
        {
            return new(false, $"switch times {before} already right");
        }

        for (int i = 0; i < wanted.Length; i++)
        {
            ranges[i].Element(ns + "endTime")!.Value = Format(wanted[i]);
            ranges[i + 1].Element(ns + "beginTime")!.Value = Format(wanted[i]);
        }

        return new(true, $"switch times {before} -> {after}");
    }

    private static int Minutes(string value)
    {
        string[] parts = value.Split(':');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture) * 60) + int.Parse(parts[1], CultureInfo.InvariantCulture);
    }

    private static string Format(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";
}
