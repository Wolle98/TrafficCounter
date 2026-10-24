using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;
using TrafficCounter.Core.Operations;
using TrafficCounter.Data;
using TrafficCounter.Detection.Onnx;
using TrafficCounter.Pipeline;
using TrafficCounter.Tracking;
using TrafficCounter.Vision;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace TrafficCounter.Service;

/// <summary>
/// Runs the counting pipeline (ADR-0012) and keeps the operations log (ADR-0024): operating period, one statistics
/// row per minute, events for source and storage state, detailed debug logging.
/// </summary>
internal sealed partial class CountingWorker(
    IOptions<ServiceOptions> options,
    ITrafficStore store,
    IDbContextFactory<TrafficCounterDbContext> dbFactory,
    DataPaths paths,
    ILoggerFactory loggerFactory,
    IHostApplicationLifetime lifetime,
    TimeProvider time) : BackgroundService
{
    private const string PreviewWindow = "TrafficCounter";
    private static readonly TimeSpan SourceLostAfter = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StartupRetryInterval = TimeSpan.FromSeconds(15);

    private readonly ILogger<CountingWorker> _logger = loggerFactory.CreateLogger<CountingWorker>();
    private readonly ServiceOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // let the host finish starting

        (int cameraId, int periodId)? session = await StartSessionAsync(stoppingToken);
        if (session is not (int cameraId, int periodId))
        {
            return; // stopped before the database became reachable
        }

        PeriodEndReason endReason = PeriodEndReason.Shutdown;
        await using var writer = new BufferedStoreWriter(
            store,
            _options.Storage.FallbackPath ?? paths.DefaultFallbackFile,
            loggerFactory.CreateLogger<BufferedStoreWriter>(),
            time);
        writer.Start();
        QualityMonitor quality = await CreateQualityMonitorAsync(cameraId, stoppingToken);

        try
        {
            using IObjectDetector detector = CreateDetector();
            await using IFrameSource source = CreateSource();
            source.Start();
            await using NetworkProbe? network = _options.Source.Type == SourceType.Rtsp && _options.NetworkProbe.Enabled
                ? new NetworkProbe(_options.NetworkProbe, loggerFactory.CreateLogger<NetworkProbe>())
                : null;
            network?.Start();

            var loop = new Thread(() => RunLoop(source, network, detector, writer, quality, cameraId, periodId, stoppingToken))
            {
                IsBackground = true,
                Name = "counting-pipeline",
            };
            loop.Start();
            await Task.Run(loop.Join, CancellationToken.None);
        }
        catch (Exception ex)
        {
            endReason = PeriodEndReason.Error;
            _logger.LogCritical(ex, "Counting stopped by a fatal error");
            writer.Enqueue(new SystemEventRecord(cameraId, periodId, time.GetLocalNow(), SystemEventKind.Error, EventSeverity.Error, ex.Message));
            Environment.ExitCode = 1;
            lifetime.StopApplication();
        }
        finally
        {
            await writer.DisposeAsync();
            await EndSessionAsync(periodId, endReason);
        }
    }

    private void RunLoop(
        IFrameSource source, NetworkProbe? network, IObjectDetector detector, BufferedStoreWriter writer, QualityMonitor quality, int cameraId, int periodId, CancellationToken stoppingToken)
    {
        using var pipeline = new CountingPipeline(detector, new ByteTracker(_options.Tracker), _options.Site, _options.Processing);
        var collector = new MinuteStatisticsCollector(cameraId, periodId, time.GetLocalNow());
        var lighting = new LightingClassifier(_options.Site.Latitude, _options.Site.Longitude);
        DriftReference? driftReference = LoadDriftReference();
        using ScheduledRecorder? recorder = CreateRecorder();

        // Recordings take the frames straight from the capture thread where possible: if counting stalls (e.g. a slow
        // detector run), the video must not get gaps (frames the counting skips are dropped before it sees them).
        var observable = source as IObservableFrameSource;
        bool recordInLoop = recorder is not null && observable is null;
        if (recorder is not null && observable is not null)
        {
            observable.CaptureObserver = frame => recorder.Offer(frame, (source as UsbFrameSource)?.ActualSettings);
        }

        SampleCollector? samples = CreateSampleCollector(cameraId, periodId, writer);
        using IObjectDetector? personDetector = samples is null ? null : CreatePersonDetector(detector);
        if (samples is not null && personDetector is null && detector is not YoloOnnxDetector { DetectsPersons: true })
        {
            // Without person boxes faces could end up unpixelated in the samples: privacy first, no samples then.
            _logger.LogWarning("Sample collection disabled: the counting model knows no people and SampleCollection:PersonModelPath is not set");
            samples = null;
        }

        bool samplesEnded = false;
        Size privateAreasFor = default;
        List<Rect> privateAreas = [];
        DateTimeOffset nextDriftCheck = time.GetLocalNow();
        DriftStatus driftStatus = DriftStatus.Ok;
        DriftStatus? driftCandidate = null;
        var reportedQuality = new SettledQualityLevel(TimeSpan.FromSeconds(_options.QualityEventHoldSeconds));
        // Only an IP camera has a fixed frame rate; a USB camera lowers it with longer exposure at night.
        var watchdog = new SourceWatchdog(time.GetLocalNow(), _options.SourceWatchdog,
            _options.Source.Type == SourceType.Rtsp ? NominalFps() : null);
        DateTimeOffset nextBlackCheck = time.GetLocalNow();
        var pane = new PaneMonitor(time.GetLocalNow(), _options.PaneMonitor);
        PreviewRenderer? preview = _options.Preview && !WindowsServiceHelpers.IsWindowsService() ? new PreviewRenderer(_options.Site) : null;
        var fps = new FrameRateMeter();
        double detectMilliseconds = 0;
        FrameSourceStatistics lastStats = source.Statistics;
        DateTimeOffset lastFrameAt = time.GetLocalNow();
        bool sourceUp = false;
        int processedInMinute = 0;
        int detectedInMinute = 0;

        void Event(SystemEventKind kind, EventSeverity severity, string message) =>
            writer.Enqueue(new SystemEventRecord(cameraId, periodId, time.GetLocalNow(), kind, severity, message));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                DateTimeOffset now = time.GetLocalNow();
                bool degradedBefore = watchdog.IsDegraded;
                bool dropsBefore = pane.HasDrops;
                if (source.TryRead(TimeSpan.FromSeconds(1), out Frame? frame))
                {
                    using (frame)
                    {
                        if (!sourceUp)
                        {
                            sourceUp = true;
                            if (!watchdog.IsStalled)
                            {
                                collector.SourceState = SourceState.Ok;
                            }

                            LogSourceConnected(source.Description, frame!.Image.Width, frame.Image.Height);
                            Event(SystemEventKind.SourceConnected, EventSeverity.Information, $"{source.Description}: {frame.Image.Width}x{frame.Image.Height}");
                        }

                        lastFrameAt = now;
                        FrameSourceStatistics stats = source.Statistics;
                        collector.AddCaptured((int)(stats.Captured - lastStats.Captured));
                        collector.AddDropped((int)(stats.Dropped - lastStats.Dropped));
                        watchdog.AddFrames(stats.Captured - lastStats.Captured);
                        lastStats = stats;
                        if (now >= nextBlackCheck)
                        {
                            nextBlackCheck = now.AddSeconds(5);
                            (double mean, double spread) = MeasureDarkness(frame!.Image);
                            watchdog.AddSample(mean, spread);
                        }

                        // Raindrops on the pane only show (and only hurt) in the dark.
                        if (pane.IsSampleDue(now) && lighting.Classify(now) is LightingCondition.Night or LightingCondition.Dusk)
                        {
                            PaneMonitorSettings p = _options.PaneMonitor;
                            pane.AddSample(RaindropDetector.CountDiscs(frame!.Image, p.DiscSigma, p.BackgroundSigma, p.MinDiscArea, p.MaxDiscArea), now);
                        }

                        if (recordInLoop)
                        {
                            recorder!.Offer(frame!, null);
                        }

                        // Reduced processing rate (ADR-0029): frames in between are captured but not processed.
                        if (!pipeline.ShouldProcess(frame!.Timestamp))
                        {
                            continue;
                        }

                        FrameResult result = pipeline.Process(frame!);
                        if (samples is not null && !samplesEnded)
                        {
                            if (!samples.IsActive(frame!.Timestamp))
                            {
                                samplesEnded = true;
                                _logger.LogWarning("Sample collection ended ({Until:yyyy-MM-dd HH:mm})", samples.Until);
                                Event(SystemEventKind.ConfigurationChanged, EventSeverity.Information, "Sample collection ended.");
                            }
                            else
                            {
                                if (frame.Image.Size() != privateAreasFor)
                                {
                                    privateAreasFor = frame.Image.Size();
                                    privateAreas = [.. _options.Site.IgnoreZones.Select(z => new Rect(
                                        (int)(z.X * privateAreasFor.Width), (int)(z.Y * privateAreasFor.Height),
                                        (int)Math.Ceiling(z.Width * privateAreasFor.Width), (int)Math.Ceiling(z.Height * privateAreasFor.Height)))];
                                }

                                Func<IReadOnlyList<BoundingBox>> persons = personDetector is null
                                    ? () => (detector as IPersonDetections)?.LastPersons ?? []
                                    : () =>
                                    {
                                        // Separate person model, only for the frames an image is taken from.
                                        personDetector.Detect(frame.Image, result.Band);
                                        return (personDetector as IPersonDetections)?.LastPersons ?? [];
                                    };
                                samples.Observe(frame, result, persons, privateAreas, lighting.Classify(frame.Timestamp));
                            }
                        }
                        fps.Add(frame!.Timestamp);
                        detectMilliseconds += result.DetectionTime.TotalMilliseconds;
                        processedInMinute++;
                        detectedInMinute += result.DetectorRan ? 1 : 0;
                        collector.AddProcessed(result.Detections.Count);
                        collector.AddPassages(result.Passages.Count);

                        foreach (VehiclePassageEvent passage in result.Passages)
                        {
                            writer.Enqueue(new PassageRecord(cameraId, periodId, passage, lighting.Classify(passage.PassedAt)));
                            LogPassage(passage);
                        }

                        if (_options.QualitySampleInterval > 0 && processedInMinute % _options.QualitySampleInterval == 1)
                        {
                            QualitySample sample = MeasureQuality(frame.Image, result);
                            collector.AddQualitySample(sample.Brightness, sample.Contrast, sample.Sharpness);
                            quality.Add(sample, frame.Timestamp);
                            if (reportedQuality.Update(quality.Current, frame.Timestamp))
                            {
                                QualityLevel level = reportedQuality.Reported;
                                EventSeverity severity = level is QualityLevel.Degraded or QualityLevel.Unusable ? EventSeverity.Warning : EventSeverity.Information;
                                _logger.Log(severity == EventSeverity.Warning ? LogLevel.Warning : LogLevel.Information,
                                    "Image quality now {Quality} (contrast {Contrast:0}, sharpness {Sharpness:0}, at line {LineContrast:0}/{LineSharpness:0})",
                                    level, sample.Contrast, sample.Sharpness, sample.LineContrast, sample.LineSharpness);
                                Event(SystemEventKind.QualityChanged, severity, $"Image quality {level}.");
                            }

                            collector.Quality = quality.Current;
                        }

                        // Drift check (ADR-0019): in daylight and while no vehicle is in view.
                        if (driftReference is not null && now >= nextDriftCheck && result.Tracking.Active.Count == 0
                            && lighting.Classify(now) is LightingCondition.Day or LightingCondition.Unknown)
                        {
                            nextDriftCheck = now.AddMinutes(_options.Drift.IntervalMinutes);
                            Vision.DriftMeasurement drift = DriftMonitor.Measure(frame.Image, driftReference, _options.Drift.Thresholds);
                            writer.Enqueue(new DriftRecord(cameraId, periodId, now, (byte)drift.Status, (float)drift.DxPixels, (float)drift.DyPixels,
                                (float)drift.RotationDegrees, (float)drift.Scale, drift.Matches, (float)drift.InlierRatio));
                            _logger.LogDebug(
                                "Drift {Status}: shift {Dx:0.0}/{Dy:0.0} px, rotation {Rotation:0.00}°, scale {Scale:0.000}, {Matches} matches, {Inliers:P0} inliers",
                                drift.Status, drift.DxPixels, drift.DyPixels, drift.RotationDegrees, drift.Scale, drift.Matches, drift.InlierRatio);
                            // A change is reported only when the very next check confirms it: in poor light single
                            // measurements can match the wrong features (a jump of hundreds of pixels at dusk). An
                            // unreliable check in between discards the candidate, otherwise two bogus readings hours
                            // apart would confirm each other.
                            if (drift.Status == DriftStatus.Unreliable)
                            {
                                driftCandidate = null;
                            }
                            else if (drift.Status != driftStatus && drift.Status != driftCandidate)
                            {
                                driftCandidate = drift.Status;
                                nextDriftCheck = now.AddMinutes(1);
                            }
                            else if (drift.Status != driftStatus)
                            {
                                driftCandidate = null;
                                driftStatus = drift.Status;
                                bool moved = drift.Status is DriftStatus.Shifted or DriftStatus.Moved;
                                string message = $"Camera position {drift.Status}: shift {drift.ShiftPixels:0} px, rotation {drift.RotationDegrees:0.0}°.";
                                _logger.Log(moved ? LogLevel.Warning : LogLevel.Information, "{Message}", message);
                                Event(SystemEventKind.CameraMoved, drift.Status == DriftStatus.Moved ? EventSeverity.Error : moved ? EventSeverity.Warning : EventSeverity.Information, message);
                            }
                            else if (drift.Status == driftStatus)
                            {
                                driftCandidate = null;
                            }
                        }

                        if (preview is not null && ShowPreview(preview, frame.Image, result, fps.FramesPerSecond, detector))
                        {
                            lifetime.StopApplication();
                            break;
                        }
                    }
                }
                else if (source is FileFrameSource { IsCompleted: true })
                {
                    _logger.LogInformation("End of video reached, stopping");
                    lifetime.StopApplication();
                    break;
                }

                if (sourceUp && now - lastFrameAt > SourceLostAfter)
                {
                    sourceUp = false;
                    collector.SourceState = SourceState.Unavailable;
                    _logger.LogWarning("{Source} delivers no frames for {Seconds:0} s", source.Description, (now - lastFrameAt).TotalSeconds);
                    Event(SystemEventKind.SourceLost, EventSeverity.Warning, $"{source.Description} delivers no frames.");
                    watchdog.NoteInterruption();
                }

                // A camera that still delivers, but only black frames or far too few, is reopened (and its minutes
                // do not count as measured) until a whole window is healthy again.
                // A source that delivers nothing at all is handled above and by the source's own reconnect.
                StallReason stallBefore = watchdog.Stall;
                if (!sourceUp)
                {
                    watchdog.Restart(now);
                }
                else if (watchdog.Evaluate(now))
                {
                    if (watchdog.IsStalled)
                    {
                        collector.SourceState = SourceState.Unavailable;
                        string message = $"{source.Description} stalled ({watchdog.Stall}), reopening it.";
                        _logger.LogError("{Message} Normal rate {Rate:0.0} fps", message, watchdog.NormalRate);
                        if (stallBefore == StallReason.None)
                        {
                            Event(SystemEventKind.SourceLost, EventSeverity.Error, message);
                        }

                        (source as IReopenableFrameSource)?.Reopen();
                    }
                    else if (stallBefore != StallReason.None)
                    {
                        collector.SourceState = sourceUp ? SourceState.Ok : SourceState.Unavailable;
                        _logger.LogInformation("{Source} delivers usable frames again", source.Description);
                        Event(SystemEventKind.SourceConnected, EventSeverity.Information, $"{source.Description} delivers usable frames again.");
                    }
                }

                // Interruptions or too few frames (ADR-0024): the minutes up to a lasting recovery are not fully measured.
                if (watchdog.IsDegraded != degradedBefore)
                {
                    string message = watchdog.IsDegraded
                        ? $"{source.Description} interrupted or too slow; minutes count as not fully measured until it is stable again."
                        : $"{source.Description} stable again.";
                    _logger.Log(watchdog.IsDegraded ? LogLevel.Warning : LogLevel.Information, "{Message} Rate {Rate:0.0} fps", message, watchdog.NormalRate);
                    Event(SystemEventKind.SourceDegraded, watchdog.IsDegraded ? EventSeverity.Warning : EventSeverity.Information, message);
                }

                // Raindrops on the pane at night (ADR-0024): the detector misses vehicles behind the bright discs.
                if (pane.Evaluate(now, lighting.Classify(now)))
                {
                    _logger.LogDebug("Pane check: median {Median} drop-like discs per frame", pane.LastMedian);
                    if (pane.HasDrops != dropsBefore)
                    {
                        string message = pane.HasDrops
                            ? $"Raindrops on the pane ({pane.LastMedian} discs per frame); minutes count as not fully measured until the view is clear again."
                            : "View through the pane clear again.";
                        _logger.Log(pane.HasDrops ? LogLevel.Warning : LogLevel.Information, "{Message}", message);
                        Event(SystemEventKind.ViewObstructed, pane.HasDrops ? EventSeverity.Warning : EventSeverity.Information, message);
                    }
                }

                collector.Degraded = watchdog.IsDegraded || pane.HasDrops;
                collector.Lighting = lighting.Classify(now);
                foreach (MinuteStatistics minute in collector.Tick(now))
                {
                    writer.Enqueue(minute);
                    LogMinute(minute.MinuteStart, minute.FramesCaptured, minute.FramesProcessed, minute.FramesDropped, minute.Detections,
                        minute.Passages, minute.SourceState, fps.FramesPerSecond, detectedInMinute > 0 ? detectMilliseconds / detectedInMinute : 0,
                        processedInMinute > 0 ? (double)detectedInMinute / processedInMinute : 0);
                    if (network?.TakeMinute() is NetworkMinute ping)
                    {
                        LogNetworkMinute(minute.MinuteStart, ping.Host, ping.Sent, ping.Lost, ping.Median, ping.P90, ping.Max);
                    }

                    processedInMinute = 0;
                    detectedInMinute = 0;
                    detectMilliseconds = 0;
                }
            }
        }
        finally
        {
            if (observable is not null)
            {
                observable.CaptureObserver = null;
            }

            foreach (VehiclePassageEvent passage in pipeline.Flush())
            {
                writer.Enqueue(new PassageRecord(cameraId, periodId, passage, lighting.Classify(passage.PassedAt)));
                LogPassage(passage);
            }

            writer.Enqueue(collector.Flush());
            if (preview is not null)
            {
                Cv2.DestroyAllWindows();
            }
        }
    }

    /// <summary>Mean brightness and its standard deviation of the whole frame, on a small copy (cheap).</summary>
    private static (double Mean, double Spread) MeasureDarkness(Mat image)
    {
        using var small = new Mat();
        Cv2.Resize(image, small, new Size(64, 36), interpolation: InterpolationFlags.Area);
        using var gray = new Mat();
        if (small.Channels() == 1)
        {
            small.CopyTo(gray);
        }
        else
        {
            Cv2.CvtColor(small, gray, ColorConversionCodes.BGR2GRAY);
        }

        Cv2.MeanStdDev(gray, out Scalar mean, out Scalar spread);
        return (mean.Val0, spread.Val0);
    }

    /// <summary>Quality of the whole band and of the region around the counting line (20 % of the band width).</summary>
    private static QualitySample MeasureQuality(Mat image, FrameResult result)
    {
        using Mat band = new(image, result.Band);
        ImageMetrics overall = ImageMetrics.Measure(band);

        int regionWidth = Math.Max(16, result.Band.Width / 5);
        int centre = (int)((result.Line.Start.X + result.Line.End.X) / 2) - result.Band.X; // the band may not start at the left edge
        int left = Math.Clamp(centre - (regionWidth / 2), 0, result.Band.Width - regionWidth);
        using Mat region = new(band, new Rect(left, 0, regionWidth, result.Band.Height));
        ImageMetrics line = ImageMetrics.Measure(region);

        return new QualitySample((float)overall.Brightness, (float)overall.Contrast, (float)overall.Sharpness, (float)line.Contrast, (float)line.Sharpness);
    }

    private SampleCollector? CreateSampleCollector(int cameraId, int periodId, BufferedStoreWriter writer)
    {
        string defaultDirectory = PrivateLocations.PrivateRoot is { } root
            ? Path.Combine(root, "datasets", "inbox")
            : Path.Combine(paths.Root, "datasets", "inbox");
        SampleCollector? collector = SampleCollector.Create(
            _options.SampleCollection, defaultDirectory, time.GetLocalNow(), loggerFactory.CreateLogger<SampleCollector>());
        if (collector is not null)
        {
            _logger.LogWarning(
                "SAMPLE COLLECTION active until {Until:yyyy-MM-dd HH:mm} (ADR-0026): anonymised images of selected vehicles go to {Directory}",
                collector.Until, collector.Directory);
            writer.Enqueue(new SystemEventRecord(cameraId, periodId, time.GetLocalNow(), SystemEventKind.ConfigurationChanged, EventSeverity.Information,
                $"Sample collection active until {collector.Until:yyyy-MM-dd HH:mm}."));
        }

        return collector;
    }

    private ScheduledRecorder? CreateRecorder()
    {
        if (!_options.Recording.Enabled)
        {
            return null;
        }

        var recorder = new ScheduledRecorder(
            _options.Recording,
            _options.Source.Usb.Fps,
            PrivateLocations.ConfigPath,
            Path.Combine(paths.Root, "recordings"),
            loggerFactory.CreateLogger<ScheduledRecorder>());
        _logger.LogWarning(
            "Recording mode enabled (ADR-0005): videos are recorded in the slots of {Schedule}",
            Path.Combine(recorder.Directory, ScheduledRecorder.ScheduleFileName));
        return recorder;
    }

    private DriftReference? LoadDriftReference()
    {
        string? path = _options.Drift.ReferencePath
            ?? (PrivateLocations.ConfigPath is { } config ? DriftReference.PathFor(config) : null);
        DriftReference? reference = path is null ? null : DriftReference.Load(path);
        if (reference is null)
        {
            _logger.LogInformation("No drift reference found ({Path}); save the calibration with static zones to enable the drift check", path ?? "-");
        }

        return reference;
    }

    private async Task<QualityMonitor> CreateQualityMonitorAsync(int cameraId, CancellationToken stoppingToken)
    {
        var monitor = new QualityMonitor();
        try
        {
            var history = await store.GetQualityHistoryAsync(cameraId, time.GetLocalNow().AddDays(-14), stoppingToken);
            monitor.Seed(history.Select(h => new QualityHistory(h.Hour, h.Contrast, h.Sharpness, h.Samples)));
            _logger.LogDebug("Quality monitor seeded with {Hours} hours of history", history.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Quality history not available; the monitor learns from scratch");
        }

        return monitor;
    }

    /// <returns>True if the user asked to quit.</returns>
    private static bool ShowPreview(PreviewRenderer preview, Mat image, FrameResult result, double fps, IObjectDetector detector)
    {
        using Mat display = preview.Render(image, result, [
            $"{fps:0.0} fps  detect {result.DetectionTime.TotalMilliseconds:0} ms  {detector.ExecutionProvider}  model {detector.ModelName}",
            "q = quit",
        ]);
        Cv2.ImShow(PreviewWindow, display);
        return Cv2.WaitKey(1) is 'q' or 27;
    }

    private async Task<(int CameraId, int PeriodId)?> StartSessionAsync(CancellationToken stoppingToken)
    {
        // At boot SQL Server may start after this service: retry until the database is reachable.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.Storage.ApplyMigrations)
                {
                    await using TrafficCounterDbContext db = await dbFactory.CreateDbContextAsync(stoppingToken);
                    await db.Database.MigrateAsync(stoppingToken);
                }

                SiteOptions site = _options.Site;
                int cameraId = await store.EnsureCameraAsync(new CameraRegistration(site.CameraName, site.DirectionAName, site.DirectionBName), stoppingToken);
                var start = new OperatingPeriodStart(
                    site.CameraName,
                    time.GetLocalNow(),
                    ConfigurationSetup.AppVersion,
                    Environment.MachineName,
                    NominalFps(),
                    Path.GetFileNameWithoutExtension(_options.Detector.ModelPath),
                    ConfigurationSetup.ConfigHash(_options));
                (int periodId, int unclean) = await store.StartPeriodAsync(cameraId, start, stoppingToken);

                _logger.LogInformation(
                    "Operating period {PeriodId} started for camera {Camera} ({Version}, config {ConfigHash})",
                    periodId, site.CameraName, start.AppVersion, start.ConfigHash);
                if (unclean > 0)
                {
                    _logger.LogWarning("{Count} previous operating period(s) had not been ended properly (crash or power loss)", unclean);
                }

                return (cameraId, periodId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Database not ready, retrying in {Delay}", StartupRetryInterval);
                await Task.Delay(StartupRetryInterval, time, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
            }
        }

        return null;
    }

    private async Task EndSessionAsync(int periodId, PeriodEndReason reason)
    {
        try
        {
            await store.EndPeriodAsync(periodId, time.GetLocalNow(), reason, CancellationToken.None);
            _logger.LogInformation("Operating period {PeriodId} ended ({Reason})", periodId, reason);
        }
        catch (Exception ex)
        {
            // The next start closes the period as "Unclean" at its last heartbeat.
            _logger.LogWarning(ex, "Could not close operating period {PeriodId}", periodId);
        }
    }

    private IObjectDetector CreateDetector()
    {
        YoloDetectorOptions detectorOptions = _options.Detector;
        // People only for anonymising training samples (ADR-0026); read from the same model output.
        detectorOptions.ReportPersons = _options.SampleCollection.Until > time.GetLocalNow();
        detectorOptions.ModelPath = ConfigurationSetup.ResolvePath(detectorOptions.ModelPath);
        return new YoloOnnxDetector(detectorOptions, loggerFactory.CreateLogger<YoloOnnxDetector>());
    }

    /// <summary>
    /// A second model for the people to pixelate in training samples, if the counting model knows none (own model,
    /// ADR-0039) and SampleCollection:PersonModelPath is set; null otherwise.
    /// </summary>
    private YoloOnnxDetector? CreatePersonDetector(IObjectDetector counting)
    {
        string? path = _options.SampleCollection.PersonModelPath;
        if (counting is YoloOnnxDetector { DetectsPersons: true } || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        YoloDetectorOptions main = _options.Detector;
        var options = new YoloDetectorOptions
        {
            ModelPath = ConfigurationSetup.ResolvePath(path),
            ExecutionProvider = main.ExecutionProvider,
            OpenVinoDevice = main.OpenVinoDevice,
            DeviceId = main.DeviceId,
            IntraOpThreads = main.IntraOpThreads,
            Resize = main.Resize,
            ReportPersons = true,
        };
        var detector = new YoloOnnxDetector(options, loggerFactory.CreateLogger<YoloOnnxDetector>());
        if (!detector.DetectsPersons)
        {
            detector.Dispose();
            throw new InvalidOperationException($"SampleCollection:PersonModelPath '{path}' has no person class.");
        }

        _logger.LogInformation("People in training samples are found with {Model}, only on the frames an image is taken from", detector.ModelName);
        return detector;
    }

    private IFrameSource CreateSource() => _options.Source.Type switch
    {
        SourceType.Usb => new UsbFrameSource(_options.Source.Usb, loggerFactory.CreateLogger<UsbFrameSource>(), time),
        SourceType.File => new FileFrameSource(
            _options.Source.File.Path ?? throw new InvalidOperationException("Source:File:Path is not set."),
            _options.Source.File.Playback),
        SourceType.Rtsp => new RtspFrameSource(_options.Source.Rtsp, loggerFactory.CreateLogger<RtspFrameSource>(), time),
        _ => throw new InvalidOperationException($"Unknown source type {_options.Source.Type}."),
    };

    private double NominalFps() => _options.Source.Type switch
    {
        SourceType.Usb => _options.Source.Usb.Fps,
        SourceType.Rtsp => _options.Source.Rtsp.Fps,
        _ => 25,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "{Source} connected, {Width}x{Height}")]
    private partial void LogSourceConnected(string source, int width, int height);

    private void LogPassage(VehiclePassageEvent passage)
    {
        TrackObservation? track = passage.Track;
        LogPassage(passage.TrackId, passage.Direction, passage.Class, passage.ClassConfidence, passage.TrailerCount, passage.LinePosition,
            passage.TrackLengthFrames, passage.PassedAt,
            passage.SpeedKmh is float kmh ? kmh.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km/h" : "none",
            track?.SpeedSamples ?? 0, track?.SpeedSeconds ?? 0, track?.TrackSeconds ?? 0, track?.FirstDistance ?? 0, track?.LastDistance ?? 0);
    }

    // The part up to "at …" is read back by TrafficCounter.Sync restore-from-log; keep it unchanged.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Passage track {TrackId}: {Direction} {VehicleClass} ({ClassConfidence:0.00}), {Trailers} trailer(s), line position {LinePosition:0.00}, {Frames} frames, at {PassedAt:HH:mm:ss.fff}; speed {Speed} from {SpeedSamples} positions over {SpeedSeconds:0.00} s in the zone, track {TrackSeconds:0.00} s from {FirstDistance:0} to {LastDistance:0} px beside the line")]
    private partial void LogPassage(int trackId, TravelDirection direction, VehicleClass vehicleClass, float? classConfidence, int trailers, float linePosition, int frames, DateTimeOffset passedAt,
        string speed, int speedSamples, double speedSeconds, double trackSeconds, float firstDistance, float lastDistance);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Network {MinuteStart:HH:mm}: ping {Host} sent {Sent}, lost {Lost}, median {Median} ms, 90 % {P90} ms, max {Max} ms")]
    private partial void LogNetworkMinute(DateTimeOffset minuteStart, string host, int sent, int lost, long? median, long? p90, long? max);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Minute {MinuteStart:HH:mm}: captured {Captured}, processed {Processed}, dropped {Dropped}, detections {Detections}, passages {Passages}, source {SourceState}, {Fps:0.0} fps, detector on {DetectorShare:P0} of processed frames, {DetectMs:0.0} ms per run")]
    private partial void LogMinute(
        DateTimeOffset minuteStart, int captured, int processed, int dropped, int detections, int passages, SourceState sourceState, double fps, double detectMs, double detectorShare);
}
