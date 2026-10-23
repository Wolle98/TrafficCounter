# Roadmap

Built up in stages. Each stage ends with a runnable state that can be used in continuous operation.
Architecture decisions are in [`docs/adr`](adr/README.md), the system design in [`architecture.md`](architecture.md).

Legend: ☐ open · ◐ in progress · ☑ done

## Stage 0 – Foundation

| # | Task | Status |
|---|---------|--------|
| 0.1 | Create ADRs and roadmap | ☑ |
| 0.2 | Toolchain: .NET 10 SDK ☑, SQL Server 2025 Express ☑, Python + Ultralytics in `tools/training/.venv` (guide: [tools/training](../tools/training/README.md)) ☑ | ☑ |
| 0.3 | Solution skeleton (`TrafficCounter.sln`), central package management (`Directory.Packages.props`), test projects (xUnit v3, Microsoft Testing Platform), CI (GitHub Actions: build + test) | ☑ |
| 0.4 | Structure the private repo (configuration, recordings, training data, Git LFS); setup script `tools/setup/New-PrivateRepository.ps1` | ☑ |
| 0.4a | Add `LICENSE` (full AGPL-3.0 text) to the public repo | ☑ |
| 0.5 | Get the camera (UGREEN FineCam Lite 4K, [ADR-0017](adr/0017-camera-and-night-operation.md)), mount it behind the pane, set the image section ☑; mounting checklist from [ADR-0023](adr/0023-weather-and-image-quality.md): water-repellent coating outside, anti-fog inside, lens hood ☐ | ◐ |
| 0.6 | **Night test** with the camera probe (1.4) – decision point: keep the webcam or switch to an IP camera. Result: the webcam stays; at night 86–90 % with the night thresholds ([ADR-0028](adr/0028-night-thresholds.md)) | ☑ |
| 0.7 | **Reference videos** (10–60 min each), recorded with the recording mode (1.5) or by the service (1.23) and counted by hand → ground truth. Categories: day, dusk, night; traffic situations from [ADR-0020](adr/0020-local-traffic-situation.md); weather from [ADR-0023](adr/0023-weather-and-image-quality.md): rain day/night, snowfall, snow cover, drops on the pane, fogged pane, reflections on a wet road, backlight. Day, night, dusk and backlight exist; extended continuously as the weather situations occur | ◐ |

## Stage 1 – Counting vehicles

Goal: every vehicle that crosses the virtual counting line is stored with a timestamp in the database.

| # | Task | Status |
|---|---------|--------|
| 1.1 | Domain model & geometry (`TrafficCounter.Core`) | ☑ |
| 1.2 | Counting logic: line, hysteresis, direction, class voting, emit by distance, reversing, `LinePosition` (`TrafficCounter.Counting`) + tests ([ADR-0020](adr/0020-local-traffic-situation.md)) | ☑ |
| 1.3 | Database schema + migration `InitialCreate` (`TrafficCounter.Data`) | ☑ |
| 1.4 | **Camera probe** + USB frame source (`TrafficCounter.Vision`): read/fix settings (exposure, focus, gain, format, fps), live image with measurements – brought forward for the night test ([ADR-0017](adr/0017-camera-and-night-operation.md)). Guide: [camera-probe.md](camera-probe.md); tested with the UGREEN webcam (1080p MJPG, 30 fps, exposure/focus/gain can be set manually). Camera selectable by DirectShow name | ☑ |
| 1.5 | Recording mode for reference videos (`record`, only explicitly, destination private repo, [ADR-0005](adr/0005-privacy.md)) + file frame source + ground truth tool (`TrafficCounter.Annotator`) and evaluation (`TrafficCounter.Evaluation`), guide: [reference-videos.md](reference-videos.md) | ☑ |
| 1.6 | Buffered DB writer with offline fallback (JSONL, automatic reload); operations log: `OperatingPeriod` with end reason and detection of unclean ends, `MinuteStatistic` as heartbeat, `SystemEvent` ([ADR-0024](adr/0024-operations-log-and-logging.md)); migration `AddOperationsLog` | ☑ |
| 1.7 | ByteTrack (`TrafficCounter.Tracking`) + tests, speed-dependent lost buffer, track stitching across occlusions ([ADR-0020](adr/0020-local-traffic-situation.md)), stitching forward along the average velocity with an edge rule and a two-wheeler/four-wheeler gate; scenario tests tracking + counting (meeting at the line, waiting with occlusion, reversing) | ☑ |
| 1.8 | YOLO ONNX detector with DirectML/CPU (`TrafficCounter.Detection.Onnx`): wide input, class-agnostic NMS, class names from the model metadata, raw and end-to-end output; export script `tools/training/export_model.py`. End-to-end test with a real model | ☑ |
| 1.9 | Counting service (`TrafficCounter.Service`) with pipeline library (`TrafficCounter.Pipeline`): configuration layers incl. private site file, Serilog (file/console/event log), operations log, waiting for the database at start-up, debug preview, video playback (`--file`); end-to-end test with a synthetic video. Guide: [operations.md](operations.md). Runs 24/7 on a notebook since 2026-10-04 | ☑ |
| 1.10 | Calibration tool for line (across the footpaths), evaluation band, kerbs and static zones (`TrafficCounter.Calibrator`), writes only the geometry into the private site file; calibrated with the webcam (incl. drift reference) | ☑ |
| 1.11 | Lighting conditions `Day`/`Dusk`/`Night` from the sun position (rough coordinates in the private site file) per passage and minute, migration `AddPassageLighting` ☑; night thresholds ☑ ([ADR-0028](adr/0028-night-thresholds.md)); dusk thresholds after evaluating the dusk videos ☐ | ◐ |
| 1.12 | Position monitoring (`DriftMonitor`): ORB reference of the static zones when saving the calibration (without image), check by day every 5 min without vehicles in the image, RANSAC estimate of shift/rotation/scale, table `DriftMeasurement`, event `CameraMoved` ([ADR-0019](adr/0019-camera-position-monitoring.md)); a change is only reported after confirmation by the next measurement. Runs in operation; no real shift detected yet | ◐ |
| 1.13 | RTSP frame source with reconnect (for a later switch to an IP camera): TCP, credentials from secrets, URL in logs without password; tested with a video file, test with a real IP camera pending | ◐ |
| 1.14 | Image quality monitoring (`QualityMonitor`): contrast/sharpness of the band and of the area around the counting line against normal values per hour (seeded from 14 days of `MinuteStatistic`, learned only from good frames), levels `Good`/`Degraded`/`Unusable` with confirmation over 3 samples, comparison with the previous hour at the start of every hour, event `QualityChanged` only after a new level has held for 3 minutes ([ADR-0023](adr/0023-weather-and-image-quality.md)). Adjusting the thresholds with real data is pending | ◐ |
| 1.15 | Regression run against reference videos (`TrafficCounter.Regression`: evaluation per video and category, comparison with the targets, band recordings are converted) ☑; live comparison against the service's stored count ☑; 7-day continuous test pending | ◐ |
| 1.16 | Trailers are counted with the towing vehicle instead of as a vehicle of their own (coupling via a direct, constant gap in the same direction and lane), number in `TrailerCount`, migration `AddTrailerCount`, shown in the preview, reported 3 s after the crossing at the earliest so that trailers entering the image late are still assigned ([ADR-0013](adr/0013-vehicle-classes.md)); check on real combinations pending | ◐ |
| 1.17 | Ignore zones for parked vehicles (driveways): detections with their ground point in a zone are discarded before tracking; calibrator `i`/`u` with a check against the road, shown in the preview ([ADR-0025](adr/0025-ignore-zones.md)) | ☑ |
| 1.18 | Thresholds for new tracks checked with the regression (reason: tractor and night vehicles that are only detected behind the counting line). Result: separate night thresholds 0.2 / 0.25 instead of 0.5 / 0.6, night accuracy from 59 % to 86 % ([ADR-0028](adr/0028-night-thresholds.md)); lower thresholds for two-wheelers tested and left off; dusk and late crossings still open | ◐ |
| 1.19 | Hand-counted reference recordings as an operating period of their own and passages with source, video and video position in the database (`Annotator import`, migration `AddDataSource`, [ADR-0027](adr/0027-hand-counted-passages.md)) | ☑ |
| 1.20 | Adjustable computing load (processing frame rate, model size, motion detection, model variant, OpenVINO/INT8, camera frame rate), the regression reports the load ([ADR-0029](adr/0029-computing-load.md)). Measured (478 vehicles, 15 fps, motion detection, notebook i5-1334U/Iris Xe): `yolo11s-640x192` 94.0 % at 24 ms/run, `yolo11s-1280x384` 93.2 % at 56 ms, `yolo11n-640x192` 88.1 % (useless at night) at 20 ms; INT8 slower than FP32 on the Iris Xe. Chosen: `yolo11s-640x192` | ☑ |
| 1.21 | PostgreSQL as a second database system (`Storage:Provider`), own migrations, `SyncId`/`UpdatedAt`, synchronisation in both directions incl. weather and database setup (`TrafficCounter.Sync`, [ADR-0030](adr/0030-postgresql-and-synchronisation.md)); set up on the NAS, the notebook writes there | ☑ |
| 1.22 | Convert reference videos to H.264 (`record` automatically, `compress` for existing ones), about 5 times smaller with the same counting accuracy ([ADR-0031](adr/0031-h264-reference-videos.md)) | ☑ |
| 1.23 | Reference recordings by the running service on a schedule, without a counting outage, straight to the hardware encoder ([ADR-0032](adr/0032-recordings-by-the-service.md)) | ☑ |

- Frame source (`IFrameSource`): RTSP, USB, video file; the capture thread always delivers the latest frame ([ADR-0011](adr/0011-frame-source-abstraction.md)).
- Detection with pre-trained YOLO (COCO) via ONNX Runtime ([ADR-0006](adr/0006-object-detection-yolo-onnx.md), [ADR-0007](adr/0007-hardware-acceleration.md)); filter on vehicle classes, region of interest (street).
- Multi-object tracking (ByteTrack-like, [ADR-0008](adr/0008-tracking-bytetrack.md)).
- Counting when crossing a counting line ([ADR-0009](adr/0009-counting-line.md)).
- Persistence: table `VehiclePassage`, operating periods (`OperatingPeriod`) ([ADR-0010](adr/0010-persistence-sql-server-ef-core.md)).
- Operation as a console app **and** Windows service ([ADR-0012](adr/0012-runtime-worker-service.md)); debug window with overlay (boxes, track IDs, line, counter).
- Calibration helper: set counting line and ROI with the mouse in a still image → writes the configuration.

**Acceptance:** on the reference videos ≥ 95 % counting accuracy by day, ≥ 85 % at night, ≥ 90 % in rain by day, ≥ 80 % in rain at night. With snow, drops on the pane and backlight, the periods are reliably marked as `limited` or `unusable` ([ADR-0023](adr/0023-weather-and-image-quality.md)). 7 days of continuous operation without crash/memory leak.

## Stage 2 – Direction of travel

The data model and the counting line already deliver the direction in stage 1 (sign of the line crossing) – stage 2 makes it reliable and visible.

- Name the directions in the configuration (e.g. "towards the town centre" / "towards the main road").
- Robustness: hysteresis around the line, minimum track length, handling of oncoming traffic/occlusion (two vehicles at the same time).
- Evaluation per direction in the reference videos. So far no direction errors in any reference video.
- Automatic drift compensation: line/ROI follow small camera movements ([ADR-0019](adr/0019-camera-position-monitoring.md)).

**Acceptance:** ≥ 98 % correct direction for correctly counted vehicles.

## Stage 3 – Vehicle types

Target classes: car, van, truck, bus, motorcycle/scooter, bicycle/e-scooter, agricultural (tractor; a combination with trailers counts as one vehicle) (+ "unknown").

- **3a** – Mapping of the COCO classes (car, truck, bus, motorcycle, bicycle) to the target classes; class per track by majority vote over all frames ([ADR-0013](adr/0013-vehicle-classes.md)). ☑
- **3b** – Training data according to [ADR-0018](adr/0018-training-data.md): public datasets from a traffic camera perspective (UA-DETRAC, MIO-TCD, Open Images; check licences and document them in `datasets/SOURCES.md`), class mapping, pre-training. Suppressing combinations (not counting trailers separately) in the counting logic.
- **3c** – Own data: data collection mode (pixelated, private; ◐ running since 2026-10-06 with up to 300 vehicles per day spread over the day, [ADR-0026](adr/0026-targeted-collection-of-training-samples.md)), semi-automatic labelling with model proposals in Label Studio (◐ set up 2026-10-11: task export with proposals, YOLO import, rules [ADR-0039](adr/0039-labelling-rules.md) accepted 2026-10-18; proposals: counting model boxes, cars, vans, trucks and buses tightened by yolo11x; ☑ the detector maps the own classes van, e-scooter and agricultural; its class `Trailer` stays unmapped, so trailer boxes are never counted, and the trailer recognition of the counting logic remains the safeguard), fine-tuning with day/night/rain images; specifically e-scooters and scooters. ☑ **Own model live since 2026-10-23 (version 0.8.0):** YOLO11s fine-tuned from COCO on own samples (`tools/training/train_model.py`); on four hand-counted hours with 385 vehicles it counted 373, the COCO model 316. Weights stay private. ◐ Labelling continues with samples picked by `select_for_labelling.py`.
- **3d** – Evaluation: confusion matrix on a held-back own test set, separated into day/night; the model version is stored per operating period.

**Acceptance:** macro F1 ≥ 0.85 over the target classes (van vs. car is usually the hardest boundary).

## Stage 4 – Analysis GUI

Blazor Server, `TrafficCounter.App` ([ADR-0014](adr/0014-gui-technology.md)). ◐ First version: overview (today and yesterday, live, with the usual daily profile), period with filters, quick ranges, directions side by side, CSV and Excel export and a print view for PDF, weekly pattern (heatmap), daily pattern (one weekday across weeks, heatmap), speed (distribution, vehicles per speed band and class, top five), operations (coverage, quality, events); multilingual (English, German, one resource file per language, [ADR-0037](adr/0037-multilingual-analysis-app.md)). Weather: import and display done (`WeatherHour`, service hourly, `Sync weather`). The app runs on the development PC; permanent hosting is deferred.

- Dashboard: today / this week, live counter.
- Time series (hour/day/week/month), daily profile, weekday comparison, heatmap weekday × hour.
- Filter by direction, vehicle class, period; marking of downtimes (from `OperatingPeriod`) and periods with limited image quality.
- **Weather data import** (DWD via Bright Sky, hourly; only the station ID in the private configuration): analyses by weather, e.g. traffic in rain vs. dry, cycling by temperature; comparing quality drops with precipitation and sunshine ([ADR-0023](adr/0023-weather-and-image-quality.md)).
- Reports: PDF and Excel/CSV export.
- Data basis: SQL views/aggregations, possibly pre-computed hourly aggregates.
- **Demo data** for development without real data: `TrafficCounter.DemoData` fills a separate database `TrafficCounter_Demo` with three weeks of synthetic traffic incl. outages and quality drops → [demo-data.md](demo-data.md). ☑

## Stage 5 – Speed

- Calibration via homography: 4+ points on the road plane with measured distances (image → metres) ([ADR-0015](adr/0015-speed-measurement.md), status *proposed*).
- Speed from the trajectory of the ground point (bottom edge of the box) in world coordinates, time base = the camera's frame timestamps, regression over several frames instead of a two-point measurement.
- Validation with test drives at known speed (GPS speedometer), stating a measurement uncertainty.
- Prerequisites: drift compensation active ([ADR-0019](adr/0019-camera-position-monitoring.md)), exposure time limited, ≥ 20 fps without frame loss in the measuring area.
- Note: the values are statistical estimates, not a calibrated measuring method.
- **First step implemented:** speed from the fitted line of the ground point at the counting line, scale per lane from the box widths of many cars (`Regression --calibrate-speed`, [ADR-0033](adr/0033-speed-scale-from-vehicle-lengths.md)). Calibrated on 2026-10-06 from 116 cars of the five reference videos. The service stores speeds since 2026-10-06 09:43. Recalibrated the same evening with the measured road marking (pinhole camera model, [ADR-0034](adr/0034-speed-scale-from-road-marking.md)): the scale from vehicle lengths was on average 4 % too high; stored speeds were rescaled (`Sync rescale-speed`). Test drives: recording by the service, marking with the speedometer reading in the annotator (`--speed true`), converted with the speedometer calibration, evaluation `Regression --speed true --live true` ([ADR-0035](adr/0035-test-drives-for-speed-validation.md)). ◐ Open: carry out the test drives and correct the scale if necessary; homography.

## Cross-cutting topics

- **Privacy** ([ADR-0005](adr/0005-privacy.md)): no images are stored in normal operation.
- **Tests:** unit tests for tracking/counting logic with synthetic tracks; regression tests against reference videos (videos private, test harness public).
- **Observability:** structured logging (Serilog), FPS/latency metrics, heartbeat and image quality per minute in the DB.

## Open questions

- Backlight in the afternoon reduces the count noticeably (one backlight hour: 86 % live). Remedies (exposure, thresholds, own model) still to be evaluated.

Settled: parking on both sides, evading via the opposite lane, waiting on the line, cyclists on footpaths are counted → [ADR-0020](adr/0020-local-traffic-situation.md). Pedestrians are not counted (the class "person" is deliberately not in the detector's class mapping). Facade on the weather side (backlight in the afternoon/evening), rain hits the pane directly depending on the wind → [ADR-0023](adr/0023-weather-and-image-quality.md). The UGREEN webcam is sufficient at night → night test (0.6).
