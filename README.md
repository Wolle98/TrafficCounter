# TrafficCounter

> [!IMPORTANT]
> **A non-commercial hobby project, still in development.** It is provided as is, **without any warranty of any kind**, express or implied, including fitness for a particular purpose; use it at your own risk. There are and will be **no binary releases**: build it from source. Licensed under the [GNU Affero General Public License v3.0](LICENSE) (see [License](#license)).

> [!NOTE]
> This project is fully coded by AI using Claude Code, partially assisted by Google Antigravity.
It is supervised by a human. This is an essential part of the project, that is not only about the traffic counting itself, but also about the capabilities of AI software engineering.

https://github.com/user-attachments/assets/749ea3e8-d1fb-415d-b7ac-64dd975edc38

<sub>Voice: ElevenLabs · Music: "Collaboration" by The_Mountain (Pixabay)</sub>

Counting road traffic with a webcam or IP camera: vehicle counts per direction and vehicle class, trailers, speed, and an analysis app in the browser. The data goes to SQL Server or PostgreSQL.

Built with C# / .NET 10, OpenCvSharp, ONNX Runtime (YOLO11; DirectML, OpenVINO, TensorRT/CUDA or CPU), EF Core, Blazor and Avalonia.

> **Privacy by design:** in normal operation no images are stored, only anonymous passage events (time, direction, class, speed). Reference recordings exist only as an explicit, time-limited mode and go to a separate private repository. See [ADR-0005](docs/adr/0005-privacy.md).

## What it does

- **Counting:** vehicles crossing a virtual counting line, with direction ([ADR-0009](docs/adr/0009-counting-line.md)).
  - Waiting, overtaking and oncoming traffic as well as occlusion are handled ([ADR-0020](docs/adr/0020-local-traffic-situation.md)).
  - Trailers count with their towing vehicle.
  - Parked cars in driveways are left out by ignore zones ([ADR-0025](docs/adr/0025-ignore-zones.md)).
- **Vehicle classes:** car, van, truck, bus, motorcycle, bicycle, e-scooter, agricultural ([ADR-0013](docs/adr/0013-vehicle-classes.md)). The pretrained COCO model knows neither vans nor e-scooters; an own model fine-tuned with `tools/training` (stage 3) adds them. Trained weights are not published.
- **Day and night:** separate confidence thresholds at night, from the sun position ([ADR-0028](docs/adr/0028-night-thresholds.md)).
- **Image quality:** monitoring per minute ([ADR-0023](docs/adr/0023-weather-and-image-quality.md), [ADR-0024](docs/adr/0024-operations-log-and-logging.md)).
- **Camera position check:** against a reference of static image features ([ADR-0019](docs/adr/0019-camera-position-monitoring.md)).
- **Speed:** statistical estimate from the ground point near the counting line ([ADR-0033](docs/adr/0033-speed-scale-from-vehicle-lengths.md), [ADR-0034](docs/adr/0034-speed-scale-from-road-marking.md)). It is not a calibrated measuring device.
  - The scale comes from a measured road marking and a pinhole camera model.
  - Without a marking, it comes from the lengths of many cars.
  - Test drives at known speed are marked in recordings and compared with the measurement ([ADR-0035](docs/adr/0035-test-drives-for-speed-validation.md)).
- **Operations log:** operating periods, one statistics row per minute and events. Gaps and degraded image quality are recorded instead of silently lowering the counts.
- **Training samples:** an optional, time-limited collection mode stores anonymised images of vehicles the current model recognises uncertainly, rarely or late, as material for an own model ([ADR-0026](docs/adr/0026-targeted-collection-of-training-samples.md)).
- **Weather:** hourly data of the nearest DWD station via Bright Sky ([ADR-0023](docs/adr/0023-weather-and-image-quality.md)). Only the station id leaves the machine.
- **Analysis app** in the browser ([ADR-0014](docs/adr/0014-gui-technology.md), Blazor Server):
  - pages: today/yesterday overview, periods with quick selection, both directions side by side, weekly heatmap, speed distribution, operations;
  - exports: CSV, Excel, printable view;
  - English and German, further languages by adding one resource file ([ADR-0037](docs/adr/0037-multilingual-analysis-app.md)).
- **Accuracy:** with the COCO model about 94 % on 653 hand-counted vehicles in reference videos (day, backlight, dusk, night), with no wrong directions; the misses were mostly e-scooters and cyclists. On four later hand-counted hours with 385 vehicles, the own model counted 373 of them, the COCO model 316. Strong afternoon backlight costs more.

## Hardware and deployment decisions

- **Development machine with an NVIDIA/AMD GPU:** ONNX Runtime with DirectML ([ADR-0007](docs/adr/0007-hardware-acceleration.md)).
- **24/7 operation on a notebook with Intel integrated graphics:** OpenVINO build (`-p:InferenceBackend=OpenVino`, [ADR-0029](docs/adr/0029-computing-load.md)).
- **NVIDIA graphics cards:** TensorRT/CUDA build (`-p:InferenceBackend=Nvidia`, [ADR-0041](docs/adr/0041-tensorrt-backend.md)), about 1.7 times as fast as DirectML with the same counts. Everything falls back to the CPU.
- **Low load:** about 25 ms per detector run on an Intel Iris Xe. The load is configurable:
  - processing frame rate independent of the camera (`MaxFps`);
  - a motion gate skips the detector on still images;
  - a reduced model input of 640×192. `yolo11s-640x192` was the best trade-off of accuracy and load.
- **Database:**
  - **SQL Server Express** on the development machine ([ADR-0010](docs/adr/0010-persistence-sql-server-ef-core.md));
  - **PostgreSQL**, for example on a NAS, for unattended operation, so the development machine does not have to run ([ADR-0030](docs/adr/0030-postgresql-and-synchronisation.md));
  - `TrafficCounter.Sync` keeps both in step. Rows have stable sync ids.
- **Runtime:** Windows service for the counter and for the analysis app ([ADR-0012](docs/adr/0012-runtime-worker-service.md)), installed with the scripts in `tools/deploy`.
- **Reference videos:**
  - recorded by the running service on a schedule, without interrupting counting ([ADR-0032](docs/adr/0032-recordings-by-the-service.md));
  - frames go straight to the hardware encoder of the GPU (Intel Quick Sync, NVENC) via ffmpeg;
  - H.264 keeps an hour at about 0.5–1.5 GB ([ADR-0031](docs/adr/0031-h264-reference-videos.md)).
- **Camera:** RTSP IP camera or USB webcam (UVC, via DirectShow, selected by name, fixed focus; [ADR-0017](docs/adr/0017-camera-and-night-operation.md)). For an IP camera with day/night scenes, the service can keep the scene times in step with sunrise and sunset ([ADR-0042](docs/adr/0042-camera-scene-times.md)).

## Prerequisites

- Windows x64, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- IDE: Visual Studio 2026 (any edition), JetBrains Rider 2025.3+ or VS Code with C# Dev Kit.
  Visual Studio 2022 cannot build .NET 10 projects.
- SQL Server (Express is sufficient) and/or PostgreSQL 15+.
- Optional:
  - [ffmpeg](https://ffmpeg.org) for H.264 reference recordings (e.g. via winget);
  - [Git LFS](https://git-lfs.com) for the private data repository;
  - Python 3.12+ for model export and training (`tools/training`).

```powershell
dotnet build TrafficCounter.sln                              # DirectML build (NVIDIA/AMD GPU or CPU)
dotnet build TrafficCounter.sln -p:InferenceBackend=OpenVino # OpenVINO build (Intel GPU/NPU or CPU)
dotnet build TrafficCounter.sln -p:InferenceBackend=Nvidia   # TensorRT/CUDA build (NVIDIA GPU or CPU)
dotnet test --solution TrafficCounter.sln                    # SQL tests need TRAFFICCOUNTER_TEST_DB
```

## Getting started

1. **Private data repository:** create it with `.\tools\setup\New-PrivateRepository.ps1` (see below).
2. **Model:** export a YOLO11 model to ONNX ([tools/training/README.md](tools/training/README.md)), e.g. `yolo11s` with 640×192 input. Put it into `models/` of the private repository.
3. **Camera:** check the camera with `TrafficCounter.CameraProbe` ([docs/camera-probe.md](docs/camera-probe.md)). Calibrate it with `TrafficCounter.Calibrator --device <n>`: evaluation band, counting line, kerbs, static and ignore zones.
4. **Counting:** run the service in the console (`dotnet run --project src/TrafficCounter.Service`) or install it as a Windows service ([docs/operations.md](docs/operations.md)).
5. **Analysis:** start the app with `dotnet run --project src/TrafficCounter.App` and open `http://localhost:5080`.

For unattended operation on another machine, `tools/deploy` builds self-contained packages:

- `Publish-TrafficCounter.ps1 -Backend OpenVino` and `Install-TrafficCounter.ps1` set up the counting service (database: SQL Server or PostgreSQL);
- `Publish-TrafficCounterApp.ps1` and `Install-TrafficCounterApp.ps1` set up the analysis app, reachable in the home network.

## Projects

| Project | Purpose |
|---|---|
| `TrafficCounter.Service` | Counting service: camera → detection → tracking → counting → database; scheduled recordings, sample collection, weather import |
| `TrafficCounter.App` | Analysis app (Blazor Server) |
| `TrafficCounter.Core`, `.Vision`, `.Detection.Onnx`, `.Tracking`, `.Counting`, `.Pipeline`, `.Data` | Building blocks of the pipeline and the data model |
| `TrafficCounter.CameraProbe` | Check camera settings, live view, record reference videos, convert them to H.264 |
| `TrafficCounter.Calibrator` | Set counting line, band, kerbs and zones; renew the reference of the camera position check |
| `TrafficCounter.Annotator` | Count reference videos by hand (ground truth) and mark speed test drives in a window (Avalonia, ADR-0043), import hand counts into the database |
| `TrafficCounter.Regression` | Accuracy against hand counts, live comparison with the service's stored counts, speed scale from car lengths, speed test drives |
| `TrafficCounter.Sync` | Set up PostgreSQL, synchronise the databases, import weather, rescale stored speeds |
| `TrafficCounter.DemoData` | Demo database with synthetic traffic for developing the app |

## Private data repository

Everything that belongs to a camera location stays out of this public repository (ADR-0002): calibration, reference videos and hand counts, trained models, database backups. It lives in a second, private repository next to this one, named like this repository plus `Private` (e.g. `TrafficCounterPrivate`). The tools find it through the user environment variable `TRAFFICCOUNTER_PRIVATE`.

Create it once with:

```powershell
.\tools\setup\New-PrivateRepository.ps1            # next to this repository, sets TRAFFICCOUNTER_PRIVATE
.\tools\setup\New-PrivateRepository.ps1 -GitHub    # additionally as a private GitHub repository (GitHub CLI)
```

The script:

- creates the folders the tools expect (`config`, `recordings`, `models`, `datasets`, `db`, `reports`, `docs`) and a README describing them;
- copies `config/site.example.json` to `config/site.json`;
- initialises Git with Git LFS for videos, images and models;
- sets `TRAFFICCOUNTER_PRIVATE` as a user variable.

Further options are `-Path <folder>` for another location and `-NoEnvironment` to leave the variable unchanged. It can be run again at any time; existing files are never overwritten. Restart terminals and the IDE afterwards so they see the variable.

The tools rely on these paths in it:

- `config/site.json`, unless `TRAFFICCOUNTER_CONFIG` points elsewhere;
- `config/site.drift.json` next to it;
- `recordings/`, with sidecar files per video: `.frames.csv`, `.json`, `.site.json`, `.gt.csv`;
- `models/`.

Passwords (database, RTSP) never go into either repository; they are kept in environment variables or user secrets, e.g. `TRAFFICCOUNTER_PG_PASSWORD`.

## Issues and contributions

This repository receives one commit per version, taken from a private working repository after an automatic check for private data ([ADR-0045](docs/adr/0045-public-snapshots.md)). Bug reports and ideas are welcome as issues here; pull requests are welcome too, but are carried over into the working repository by hand and appear here with the next version.

## Documentation

- [Roadmap](docs/roadmap.md): stages 1–5, status and acceptance criteria
- [Architecture](docs/architecture.md): pipeline, project layout, data model
- [Architecture Decision Records](docs/adr/README.md): all decisions with their reasons
- [Operation](docs/operations.md): service, calibration, load settings, notebook installation, PostgreSQL, weather, recordings, analysis app
- [Camera probe and night test](docs/camera-probe.md)
- [Reference videos, ground truth, regression](docs/reference-videos.md)
- [Demo data](docs/demo-data.md)
- [Model export and training](tools/training/README.md)

## License

Copyright (C) 2026 Wolle98

This program is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General
Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any
later version. It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the
implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the [license](LICENSE) for details;
the reasons for the AGPL are in [ADR-0004](docs/adr/0004-license-agpl.md).

**Additional permission under AGPL-3.0 section 7:** if you modify this program, or any covered work, by linking or
combining it with Microsoft DirectML (Microsoft.AI.DirectML), the Microsoft.Data.SqlClient SNI runtime,
Microsoft.Identity.Client.NativeInterop, or the NVIDIA CUDA, cuDNN or TensorRT libraries (or modified versions of
these libraries), containing parts covered by the terms of their respective licenses, the licensors of this program
grant you additional permission to convey the resulting work ([ADR-0044](docs/adr/0044-license-exception-proprietary-runtimes.md)). Corresponding Source for a non-source form of such a
combination shall not include the source code for the parts of those libraries used as well as that of the covered
work.

Binary packages are not published. Whoever conveys built packages must also pass on the license notices of the bundled
third-party components (for example the Apache-2.0 NOTICE files and the LGPL notice of the FFmpeg library shipped with
OpenCV).
