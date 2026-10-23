using TrafficCounter.Core;
using TrafficCounter.Core.Operations;
using TrafficCounter.Data;

namespace TrafficCounter.DemoData;

public sealed record DemoDataOptions
{
    /// <summary>First local day; generation covers <see cref="Days"/> full days from 00:00.</summary>
    public required DateOnly FirstDay { get; init; }

    public int Days { get; init; } = 21;

    public int Seed { get; init; } = 42;

    /// <summary>Coarse coordinates for day/dusk/night (the demo does not need the real site).</summary>
    public double Latitude { get; init; } = 51.0;

    public double Longitude { get; init; } = 7.0;

    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Local;

    public float NominalFps { get; init; } = 20;
}

/// <summary>
/// Generated rows. Foreign keys to <see cref="OperatingPeriod"/> hold the index into <see cref="Periods"/>
/// and <c>CameraId</c> is 0; <see cref="DemoDataWriter"/> replaces both with the database keys.
/// </summary>
public sealed class DemoDataSet
{
    public List<OperatingPeriod> Periods { get; } = [];

    public List<VehiclePassage> Passages { get; } = [];

    public List<MinuteStatistic> Minutes { get; } = [];

    public List<SystemEvent> Events { get; } = [];

    public List<DriftMeasurement> DriftMeasurements { get; } = [];
}

/// <summary>
/// Creates synthetic but plausible operating data: passages after <see cref="TrafficProfile"/> and
/// <see cref="SpeedModel"/>, minute statistics, drift checks and a few incidents (update restart, crash,
/// camera outage, heavy rain, shifted camera) so that reports can show gaps and quality markers.
/// </summary>
public sealed class DemoDataGenerator(DemoDataOptions options)
{
    // Kerbs along the counting line as calibrated at the site (the line runs from property line to property line).
    private const double FarKerb = 0.14;
    private const double NearKerb = 0.67;
    private const double VisibleRoadMetres = 25;

    private readonly Random _random = new(options.Seed);
    private readonly LightingClassifier _lighting = new(options.Latitude, options.Longitude);

    public DemoDataSet Generate()
    {
        DateTimeOffset start = LocalMidnight(options.FirstDay);
        DateTimeOffset end = LocalMidnight(options.FirstDay.AddDays(options.Days));
        List<Incident> incidents = Incidents();
        Dictionary<VehicleClass, double> shapeMean = ShapeMeans(start, end);

        var data = new DemoDataSet();
        int period = -1;
        int trackId = 0;
        double directionAShare = 0.5;
        double dayFactor = 1;
        double hourFactor = 1;
        (int A, int B) busDelay = (0, 0);
        QualityLevel lastQuality = QualityLevel.Good;
        DriftState drift = DriftState.Ok;

        for (DateTimeOffset minute = start; minute < end; minute = minute.AddMinutes(1))
        {
            DateTime local = TimeZoneInfo.ConvertTime(minute, options.TimeZone).DateTime;
            DateTimeOffset localMinute = TimeZoneInfo.ConvertTime(minute, options.TimeZone);

            if (local.TimeOfDay == TimeSpan.Zero || period < 0)
            {
                directionAShare = Math.Clamp(TrafficProfile.DirectionAShare + (Normal() * 0.02), 0.39, 0.50);
                dayFactor = Math.Max(0.8, 1 + (Normal() * 0.05));
            }

            if (local.Minute == 0 || period < 0)
            {
                hourFactor = Math.Max(0.7, 1 + (Normal() * 0.08));
                busDelay = (_random.Next(-2, 3), _random.Next(-2, 4)); // about :13–:17 and :43–:48
            }

            if (Covering(incidents, IncidentKind.Down, minute) is { } down)
            {
                // The counter is off: close the running period once, nothing else is recorded.
                if (period >= 0 && data.Periods[period].EndedAt is null)
                {
                    OperatingPeriod p = data.Periods[period];
                    p.EndReason = down.EndReason;
                    if (down.EndReason == PeriodEndReason.Shutdown)
                    {
                        p.EndedAt = minute;
                        AddEvent(data, period, minute, SystemEventKind.Stopped, EventSeverity.Information, "Counter stopped (update).");
                    }
                    else
                    {
                        p.EndedAt = p.LastHeartbeatAt;
                    }
                }

                continue;
            }

            if (period < 0 || data.Periods[period].EndedAt is not null)
            {
                period = StartPeriod(data, localMinute, period >= 0 ? data.Periods[period].EndReason : null);
                trackId = 0;
            }

            bool sourceOk = Covering(incidents, IncidentKind.CameraLost, minute) is null;
            QualityLevel quality = Covering(incidents, IncidentKind.Unusable, minute) is not null ? QualityLevel.Unusable
                : Covering(incidents, IncidentKind.Degraded, minute) is not null ? QualityLevel.Degraded
                : QualityLevel.Good;
            LightingCondition lighting = _lighting.Classify(minute.AddSeconds(30));

            if (!sourceOk && data.Minutes.Count > 0 && data.Minutes[^1].SourceState == SourceState.Ok)
            {
                AddEvent(data, period, minute, SystemEventKind.SourceLost, EventSeverity.Warning, "USB camera delivers no frames.");
            }
            else if (sourceOk && data.Minutes.Count > 0 && data.Minutes[^1].SourceState == SourceState.Unavailable)
            {
                AddEvent(data, period, minute, SystemEventKind.SourceConnected, EventSeverity.Information, "USB camera: 1920x1080");
            }

            if (sourceOk && quality != lastQuality)
            {
                AddEvent(data, period, minute, SystemEventKind.QualityChanged,
                    quality == QualityLevel.Good ? EventSeverity.Information : EventSeverity.Warning, $"Image quality {quality}.");
                lastQuality = quality;
            }

            // Missed vehicles: heavy rain on the window, darkness.
            double detected = quality switch
            {
                QualityLevel.Unusable => 0.6,
                QualityLevel.Degraded => 0.95,
                _ => lighting == LightingCondition.Night ? 0.97 : 1,
            };

            int passages = 0;
            if (sourceOk)
            {
                double baseRate = TrafficProfile.VehiclesPerHour(local) * dayFactor * hourFactor;
                foreach ((VehicleClass vehicleClass, double share) in TrafficProfile.ClassShares)
                {
                    double perMinute = baseRate * share * TrafficProfile.ClassShape(vehicleClass, local) / shapeMean[vehicleClass] / 60;
                    for (int n = Poisson(perMinute); n > 0; n--)
                    {
                        if (_random.NextDouble() >= detected)
                        {
                            continue;
                        }

                        DateTimeOffset passedAt = minute.AddMilliseconds(_random.Next(60_000));
                        data.Passages.Add(Passage(period, ++trackId, passedAt, vehicleClass, directionAShare));
                        passages++;
                    }
                }

                // Buses run to the timetable, a minute or two early or late.
                foreach (TravelDirection busDirection in new[] { TravelDirection.A, TravelDirection.B })
                {
                    if (TrafficProfile.BusMinute(local, busDirection) is int busMinute
                        && local.Minute == busMinute + (busDirection == TravelDirection.A ? busDelay.A : busDelay.B)
                        && _random.NextDouble() < detected)
                    {
                        DateTimeOffset passedAt = minute.AddMilliseconds(_random.Next(60_000));
                        data.Passages.Add(Passage(period, ++trackId, passedAt, VehicleClass.Bus, busDirection == TravelDirection.A ? 1 : 0));
                        passages++;
                    }
                }
            }

            data.Minutes.Add(Minute(period, minute, sourceOk, quality, lighting, passages, local));
            data.Periods[period].LastHeartbeatAt = minute.AddMinutes(1);

            drift = UpdateDrift(data, period, minute, incidents, drift, sourceOk, lighting);
        }

        OperatingPeriod last = data.Periods[period];
        if (last.EndedAt is null)
        {
            last.EndedAt = end;
            last.EndReason = PeriodEndReason.Shutdown;
            AddEvent(data, period, end, SystemEventKind.Stopped, EventSeverity.Information, "Counter stopped.");
        }

        data.Passages.Sort((a, b) => a.PassedAt.CompareTo(b.PassedAt));
        AssignSpeeds(data.Passages);
        return data;
    }

    private DateTimeOffset LocalMidnight(DateOnly day)
    {
        DateTime midnight = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, options.TimeZone.GetUtcOffset(midnight));
    }

    /// <summary>Incidents at fixed days and times, so every demo database shows each kind once.</summary>
    private List<Incident> Incidents()
    {
        List<Incident> incidents = [];
        void Add(int day, TimeOnly from, TimeOnly to, IncidentKind kind, PeriodEndReason? reason = null)
        {
            if (day < options.Days)
            {
                DateOnly date = options.FirstDay.AddDays(day);
                DateTime f = date.ToDateTime(from);
                DateTime t = date.ToDateTime(to);
                incidents.Add(new Incident(
                    new DateTimeOffset(f, options.TimeZone.GetUtcOffset(f)), new DateTimeOffset(t, options.TimeZone.GetUtcOffset(t)), kind, reason));
            }
        }

        Add(4, new(10, 12), new(10, 14), IncidentKind.Down, PeriodEndReason.Shutdown);    // software update
        Add(9, new(3, 41), new(6, 55), IncidentKind.Down, PeriodEndReason.Unclean);       // crash, restarted in the morning
        Add(11, new(9, 30), new(18, 0), IncidentKind.Shifted);                             // window cleaned, camera nudged
        Add(13, new(14, 20), new(14, 48), IncidentKind.CameraLost);                        // USB disconnect
        Add(16, new(15, 0), new(18, 0), IncidentKind.Degraded);                            // rain on the window
        Add(16, new(16, 10), new(16, 40), IncidentKind.Unusable);                          // downpour
        return incidents;
    }

    private static Incident? Covering(List<Incident> incidents, IncidentKind kind, DateTimeOffset time) =>
        incidents.FirstOrDefault(i => i.Kind == kind && time >= i.From && time < i.To);

    /// <summary>Mean of each class shape weighted by the base rate, so that the class shares hold overall.</summary>
    private Dictionary<VehicleClass, double> ShapeMeans(DateTimeOffset start, DateTimeOffset end)
    {
        double total = 0;
        Dictionary<VehicleClass, double> sums = TrafficProfile.ClassShares.Keys.ToDictionary(c => c, _ => 0.0);
        for (DateTimeOffset minute = start; minute < end; minute = minute.AddMinutes(1))
        {
            DateTime local = TimeZoneInfo.ConvertTime(minute, options.TimeZone).DateTime;
            double rate = TrafficProfile.VehiclesPerHour(local);
            total += rate;
            foreach (VehicleClass vehicleClass in sums.Keys)
            {
                sums[vehicleClass] += rate * TrafficProfile.ClassShape(vehicleClass, local);
            }
        }

        return sums.ToDictionary(s => s.Key, s => s.Value / total);
    }

    private int StartPeriod(DemoDataSet data, DateTimeOffset startedAt, PeriodEndReason? previousEnd)
    {
        data.Periods.Add(new OperatingPeriod
        {
            StartedAt = startedAt,
            LastHeartbeatAt = startedAt,
            NominalFps = options.NominalFps,
            AppVersion = "1.0.0-demo",
            MachineName = "DEMO",
            ModelName = "yolo11s-1280x384.onnx",
            ConfigHash = "demo",
        });
        int index = data.Periods.Count - 1;
        if (previousEnd == PeriodEndReason.Unclean)
        {
            AddEvent(data, index, startedAt, SystemEventKind.UncleanShutdownDetected, EventSeverity.Warning,
                "Previous period ended without shutdown; closed at its last heartbeat.");
        }

        AddEvent(data, index, startedAt, SystemEventKind.Started, EventSeverity.Information, "Counter started (demo data).");
        AddEvent(data, index, startedAt.AddSeconds(3), SystemEventKind.SourceConnected, EventSeverity.Information, "USB camera: 1920x1080");
        return index;
    }

    private VehiclePassage Passage(int period, int trackId, DateTimeOffset passedAt, VehicleClass vehicleClass, double directionAShare)
    {
        TravelDirection direction = _random.NextDouble() < directionAShare ? TravelDirection.A : TravelDirection.B;
        return new VehiclePassage
        {
            OperatingPeriodId = period,
            PassedAt = passedAt,
            Direction = direction,
            VehicleClass = vehicleClass,
            ClassConfidence = (float)Math.Round(vehicleClass switch
            {
                VehicleClass.Car => 0.75 + (_random.NextDouble() * 0.22),
                VehicleClass.Van or VehicleClass.Agricultural => 0.5 + (_random.NextDouble() * 0.35),
                _ => 0.6 + (_random.NextDouble() * 0.3),
            }, 3),
            LinePosition = (float)Math.Round(LinePosition(vehicleClass, direction), 3),
            TrailerCount = Trailers(vehicleClass),
            TrackId = trackId,
            Lighting = _lighting.Classify(passedAt),
        };
    }

    /// <summary>Tractors mostly tow one or two trailers; some cars and trucks tow one.</summary>
    private byte Trailers(VehicleClass vehicleClass)
    {
        double r = _random.NextDouble();
        return vehicleClass switch
        {
            VehicleClass.Agricultural => r < 0.15 ? (byte)2 : r < 0.6 ? (byte)1 : (byte)0,
            VehicleClass.Truck => r < 0.08 ? (byte)1 : (byte)0,
            VehicleClass.Car or VehicleClass.Van => r < 0.0015 ? (byte)1 : (byte)0, // about 1 in 500 vehicles
            _ => 0,
        };
    }

    /// <summary>
    /// Position along the counting line (0 = far property line, 1 = near one): direction A (far lane)
    /// around 0.37, B (near lane) around 0.44, each ±0.05 (evading parked cars included); large vehicles a little
    /// further to the middle; 18 % of the cyclists on one of the sidewalks, the others at the edge of their lane.
    /// </summary>
    private double LinePosition(VehicleClass vehicleClass, TravelDirection direction)
    {
        bool near = direction == TravelDirection.B;
        if (vehicleClass == VehicleClass.Bicycle)
        {
            if (_random.NextDouble() < 0.18)
            {
                return _random.NextDouble() < 0.5
                    ? 0.02 + (_random.NextDouble() * (FarKerb - 0.03))
                    : NearKerb + 0.01 + (_random.NextDouble() * 0.2);
            }

            return Math.Clamp((near ? 0.50 : 0.30) + (Normal() * 0.05), FarKerb, NearKerb);
        }

        double centre = vehicleClass is VehicleClass.Truck or VehicleClass.Bus or VehicleClass.Agricultural
            ? (near ? 0.45 : 0.42)
            : (near ? 0.44 : 0.37);
        return Math.Clamp(centre + (Normal() * 0.05), FarKerb, NearKerb);
    }

    private void AssignSpeeds(List<VehiclePassage> passages)
    {
        DateTime Local(VehiclePassage p) => TimeZoneInfo.ConvertTime(p.PassedAt, options.TimeZone).DateTime;
        var model = new SpeedModel(passages
            .Where(p => SpeedModel.IsMotorVehicle(p.VehicleClass!.Value))
            .Select(p => (p.VehicleClass!.Value, Local(p))));
        var random = new Random(options.Seed + 1);
        foreach (VehiclePassage passage in passages)
        {
            double speed = model.Draw(random, passage.VehicleClass!.Value, Local(passage));
            passage.SpeedKmh = (float)Math.Round(speed, 1);
            passage.TrackLengthFrames = (int)Math.Round(VisibleRoadMetres / (speed / 3.6) * options.NominalFps * (0.8 + (random.NextDouble() * 0.3)));
        }
    }

    private MinuteStatistic Minute(
        int period, DateTimeOffset minute, bool sourceOk, QualityLevel quality, LightingCondition lighting, int passages, DateTime local)
    {
        if (!sourceOk)
        {
            return new MinuteStatistic
            {
                OperatingPeriodId = period,
                MinuteStart = minute,
                SourceState = SourceState.Unavailable,
                Quality = QualityLevel.Unknown,
                Lighting = lighting,
            };
        }

        int frames = (int)Math.Round(options.NominalFps * 60) - _random.Next(0, 4);
        int dropped = _random.NextDouble() < 0.05 ? _random.Next(1, 20) : 0;
        int parked = TrafficProfile.IsNight(local) ? 9 : 6;
        (double brightness, double contrast, double sharpness) = lighting switch
        {
            LightingCondition.Day => (125, 48, 180),
            LightingCondition.Dusk => (65, 30, 90),
            _ => (17, 14, 35),
        };
        double factor = quality switch
        {
            QualityLevel.Unusable => 0.35,
            QualityLevel.Degraded => 0.7,
            _ => 1,
        };

        return new MinuteStatistic
        {
            OperatingPeriodId = period,
            MinuteStart = minute,
            FramesCaptured = frames,
            FramesProcessed = frames - dropped,
            FramesDropped = dropped,
            Detections = ((frames - dropped) * parked) + (passages * 70),
            Passages = passages,
            SourceState = SourceState.Ok,
            Quality = quality,
            Brightness = (float)Math.Round(brightness * (quality == QualityLevel.Good ? 1 : 0.85) * (1 + (Normal() * 0.05)), 1),
            Contrast = (float)Math.Round(contrast * factor * (1 + (Normal() * 0.05)), 1),
            Sharpness = (float)Math.Round(sharpness * factor * (1 + (Normal() * 0.08)), 1),
            Lighting = lighting,
        };
    }

    private DriftState UpdateDrift(
        DemoDataSet data, int period, DateTimeOffset minute, List<Incident> incidents, DriftState previous, bool sourceOk, LightingCondition lighting)
    {
        if (!sourceOk || minute.Minute % 5 != 0)
        {
            return previous;
        }

        bool shifted = Covering(incidents, IncidentKind.Shifted, minute) is not null;
        int matches = lighting == LightingCondition.Night ? _random.Next(15, 70) : _random.Next(280, 520);
        DriftState state = matches < 40 ? DriftState.Unreliable : shifted ? DriftState.Shifted : DriftState.Ok;
        data.DriftMeasurements.Add(new DriftMeasurement
        {
            OperatingPeriodId = period,
            MeasuredAt = minute,
            Status = (byte)state,
            DxPixels = (float)Math.Round((shifted ? 6.5 : 0) + (Normal() * 0.3), 2),
            DyPixels = (float)Math.Round((shifted ? -2.1 : 0) + (Normal() * 0.3), 2),
            RotationDegrees = (float)Math.Round((shifted ? 0.4 : 0) + (Normal() * 0.03), 3),
            Scale = (float)Math.Round(1 + (Normal() * 0.001), 4),
            Matches = matches,
            InlierRatio = (float)Math.Round(state == DriftState.Unreliable ? 0.3 + (_random.NextDouble() * 0.3) : 0.8 + (_random.NextDouble() * 0.15), 3),
        });

        if (state is DriftState.Shifted && previous is not DriftState.Shifted)
        {
            AddEvent(data, period, minute, SystemEventKind.CameraMoved, EventSeverity.Warning, "Camera shifted by 6.8 px, 0.4°; compensated.");
        }
        else if (state is DriftState.Ok && previous is DriftState.Shifted)
        {
            AddEvent(data, period, minute, SystemEventKind.ConfigurationChanged, EventSeverity.Information, "Recalibrated; drift reference renewed.");
        }

        return state == DriftState.Unreliable ? previous : state;
    }

    private static void AddEvent(DemoDataSet data, int period, DateTimeOffset time, SystemEventKind kind, EventSeverity severity, string message) =>
        data.Events.Add(new SystemEvent { OperatingPeriodId = period, OccurredAt = time, Kind = kind, Severity = severity, Message = message });

    private int Poisson(double lambda)
    {
        double limit = Math.Exp(-lambda);
        double product = _random.NextDouble();
        int count = 0;
        while (product > limit)
        {
            count++;
            product *= _random.NextDouble();
        }

        return count;
    }

    private double Normal() =>
        Math.Sqrt(-2 * Math.Log(1 - _random.NextDouble())) * Math.Cos(2 * Math.PI * _random.NextDouble());

    private enum IncidentKind
    {
        Down,
        CameraLost,
        Degraded,
        Unusable,
        Shifted,
    }

    private enum DriftState : byte
    {
        Unreliable = 0,
        Ok = 1,
        Shifted = 2,
    }

    private sealed record Incident(DateTimeOffset From, DateTimeOffset To, IncidentKind Kind, PeriodEndReason? EndReason);
}
