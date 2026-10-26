# Architecture Decision Records

Format and process: [ADR-0001](0001-use-adrs.md) · Template: [0000-template.md](0000-template.md)

| No. | Title | Status | Stage |
|-----|-------|--------|-------|
| [0001](0001-use-adrs.md) | Record architecture decisions as ADRs | accepted, language rule superseded by 0036 | 0 |
| [0002](0002-repository-split.md) | Split into a public and a private repository | accepted, extended by 0045 | 0 |
| [0003](0003-platform-dotnet.md) | Platform .NET 10 (LTS), C#, Windows x64 | accepted | 0 |
| [0004](0004-license-agpl.md) | License AGPL-3.0 | accepted | 0 |
| [0005](0005-privacy.md) | Privacy – no image storage in normal operation | accepted | all |
| [0006](0006-object-detection-yolo-onnx.md) | Object detection with YOLO via ONNX Runtime – from stage 1 | accepted | 1 |
| [0007](0007-hardware-acceleration.md) | Hardware acceleration via DirectML with CPU fallback | accepted | 1 |
| [0008](0008-tracking-bytetrack.md) | Multi-object tracking with an own ByteTrack implementation | accepted | 1 |
| [0009](0009-counting-line.md) | Counting via a virtual counting line, direction from the crossing | accepted | 1, 2 |
| [0010](0010-persistence-sql-server-ef-core.md) | Persistence in SQL Server Express with EF Core, individual events | accepted | 1 |
| [0011](0011-frame-source-abstraction.md) | Abstraction of frame sources, "latest frame" strategy | accepted | 1 |
| [0012](0012-runtime-worker-service.md) | Runtime model worker service (console and Windows service) | accepted | 1 |
| [0013](0013-vehicle-classes.md) | Vehicle classes – COCO mapping, then an own model | accepted, trailer box superseded by 0039, extended by 0046 | 3 |
| [0014](0014-gui-technology.md) | Technology of the analysis GUI | accepted | 4 |
| [0015](0015-speed-measurement.md) | Speed measurement via homography | accepted | 5 |
| [0016](0016-opencvsharp.md) | OpenCvSharp4 as the OpenCV binding | accepted | 1 |
| [0017](0017-camera-and-night-operation.md) | Camera and night operation – start with a USB webcam, night test as decision point | accepted | 0, 1 |
| [0018](0018-training-data.md) | Training data – public datasets plus own images | accepted | 3 |
| [0019](0019-camera-position-monitoring.md) | Monitoring the camera position and drift compensation | accepted | 1, 2, 5 |
| [0020](0020-local-traffic-situation.md) | Local traffic situation – parking, evading, waiting, occlusion, cyclists on footpaths | accepted | 1, 2, 5 |
| [0021](0021-solution-format-sln.md) | Classic solution format `.sln` instead of `.slnx` | accepted | 0 |
| [0022](0022-issue-tracking.md) | Issue tracking on GitHub, strictly separated into public and private | accepted, points 4, 6, 7 superseded by 0045 | all |
| [0023](0023-weather-and-image-quality.md) | Weather, backlight and image quality – measures, monitoring, weather data | accepted | 1, 3, 4 |
| [0024](0024-operations-log-and-logging.md) | Operations log in the database and debug logs | accepted | 1 |
| [0025](0025-ignore-zones.md) | Ignore zones for parked vehicles | accepted | 1 |
| [0026](0026-targeted-collection-of-training-samples.md) | Targeted collection of training samples in operation | accepted | 3 |
| [0027](0027-hand-counted-passages.md) | Hand-counted passages in the database | accepted | 1, 4 |
| [0028](0028-night-thresholds.md) | Separate confidence thresholds for the night | accepted | 1 |
| [0029](0029-computing-load.md) | Adjustable computing load for weaker hardware | accepted | 1 |
| [0030](0030-postgresql-and-synchronisation.md) | PostgreSQL on the NAS and synchronisation with SQL Server | accepted | 1, 4 |
| [0031](0031-h264-reference-videos.md) | Convert reference videos to H.264 with ffmpeg afterwards | accepted | 1 |
| [0032](0032-recordings-by-the-service.md) | Reference recordings by the running service | accepted | 1, 3 |
| [0033](0033-speed-scale-from-vehicle-lengths.md) | Speed first with a scale from vehicle lengths | accepted, scale replaced by 0034 | 5 |
| [0034](0034-speed-scale-from-road-marking.md) | Speed scale from a measured road marking | accepted | 5 |
| [0035](0035-test-drives-for-speed-validation.md) | Test drives for speed validation via recording and marking afterwards | accepted | 5 |
| [0036](0036-documentation-in-english.md) | Documentation in English | accepted, app UI point superseded by 0037 | all |
| [0037](0037-multilingual-analysis-app.md) | Multilingual analysis app with one resource file per language | accepted | 4 |
| [0038](0038-versioning.md) | One version for all programs, raised with every deployment | accepted | all |
| [0039](0039-labelling-rules.md) | Labelling rules for own training images (trailer class, bicycle with rider) | accepted | 3 |
| [0040](0040-band-sides.md) | Left and right edge of the evaluation band | accepted | 1 |
| [0041](0041-tensorrt-backend.md) | TensorRT and CUDA backend for NVIDIA graphics cards | accepted | 3 |
| [0042](0042-camera-scene-times.md) | Day/night scene times of the IP camera follow sunrise and sunset | accepted | 2 |
| [0043](0043-avalonia-annotator.md) | Avalonia for the annotator | accepted | 1 |
| [0044](0044-license-exception-proprietary-runtimes.md) | Additional AGPL permission for proprietary runtime libraries | accepted | 0 |
| [0045](0045-public-snapshots.md) | Public repository as checked snapshots of a private working repository | accepted | 0 |
| [0046](0046-two-wheelers-and-split-tracks.md) | Two-wheelers are never trailers; one vehicle split into two tracks is counted once | accepted | 1 |
| [0047](0047-trailer-class-tracked.md) | The own model's trailers are tracked and only counted with their towing vehicle | accepted | 3 |
