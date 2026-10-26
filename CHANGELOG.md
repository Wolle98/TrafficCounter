# Changelog

All programs share one version (ADR-0038). Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [Semantic Versioning](https://semver.org/) with a 0.x major while the project is in development.

## [Unreleased]

## [0.10.9] – 2026-10-26

### Changed

- Regression `--trace` shows the trailer count of each passage.

### Fixed

- Trailers detected by the own model as class `Trailer` were dropped, so a car with a trailer was stored without it. They are now tracked and only counted with the vehicle in front (`TrailerCount`), never on their own (ADR-0047).

## [0.10.8] – 2026-10-26

### Added

- `TrafficCounter.Sync set-trailers`: corrects the number of trailers of single passages in both databases; `add-alongside` no longer copies the trailers of the original.
- `tools/deploy/Sync-FillDegraded.ps1`: sync, `fill-degraded` from a second camera's database over the last hours and sync again, for a scheduled task.

### Changed

- Architecture diagram: TensorRT/CUDA, band edges, own model classes, two-wheelers and split tracks, camera scene times, PostgreSQL as main database, hourly fill from a second camera.

### Fixed

- Cyclists riding close behind each other are no longer merged as one bicycle with a trailer: two-wheelers are never trailers and never tow one (ADR-0046).
- A long vehicle split into two tracks (e.g. a bus close to the camera) is counted once: a second motor-vehicle passage within 0.4 s in the same direction and lane is dropped (`CountingSettings.DuplicateSeconds`, `DuplicateLineTolerance`, ADR-0046).

## [0.10.7] – 2026-10-24

### Added

- `TRAFFICCOUNTER_DB_PROVIDER` sets the machine's main database for the analysis app and the tools (Regression `--live`, Annotator `import`, Sync `weather`); default SQL Server. The analysis app also takes the PostgreSQL connection from `TRAFFICCOUNTER_PG`.

### Changed

- Analysis app, operations page: image quality is called good / okay / limited (was good / limited / unusable), since minutes of low contrast still count as measured.
- Regression `--trace` uses the processing options (`--max-fps`, `--motion-gate`), marks frames the motion gate skipped and shows the speed details of each passage.

### Fixed

- `fill-degraded` takes over the end of the second camera's copied operating periods; before, a period copied while it was running stayed "running" in the target for good.

## [0.10.6] – 2026-10-24

### Changed

- Speed: the shortest observation in the speed zone is 0.25 s instead of 0.4 s (`Site:SpeedMinSeconds`), so fast cars on the near lane get a speed too; still at least five positions. Test drives unchanged (NC400 25/25, mean absolute error 6.7 %; UGREEN 20/20, 4.8 %); with the live settings a few more cars get a speed (NC400 94 → 95, UGREEN 88 → 93 in one hour).
- Operations events start with the camera's name (`Source:Name`, else the USB device name or the source description), e.g. "NC400: Camera position Moved: …".
- Analysis app: a date range chosen on one page (date fields or quick buttons) is kept when switching to another page.
- Analysis app, speed page: the table by speed and class shows plain counts per class; only "all" has its share, in a column of its own; a last row gives the column totals.

## [0.10.5] – 2026-10-24

### Added

- Counting service log: every passage line also gives its speed (or "none"), the positions and time inside the speed measuring zone, the track's duration and where it was first and last seen, so a missing speed can be explained.
- Counting service preview: the speed measuring zone is shown as a very transparent red strip.
- `TrafficCounter.Sync restore-from-log`: puts a counting machine's passages and minutes back from its service log for minutes the databases no longer have, e.g. after `fill-degraded` replaced them with the counts of a second camera running another model (without speeds and image quality, which the log does not have).

## [0.10.4] – 2026-10-24

### Changed

- Analysis app, speed page: columns "above 35" and "above 40" by direction and time of day; the table by speed and class also gives each count's share of its column (two decimals). Numeric columns of all tables are centred under their headings.
- Counting service preview window: the status line also names the model in use, next to the execution provider.

## [0.10.3] – 2026-10-23

### Changed

- Analysis app: percentages with two decimals (also in the Excel export), so a small share such as the vehicles above 50 km/h no longer shows as 0 % next to the fastest vehicles.

### Fixed

- Bug issue form: "None" is reserved in GitHub dropdowns; the option is now "No impact".

## [0.10.2] – 2026-10-23

### Added

- `AGENTS.md` pointing other coding agents to `CLAUDE.md`.

### Changed

- README video uploaded again in the public repository.
- `Publish-PublicSnapshot.ps1` stops if the public repository has commits after the last snapshot that are not yet in the working repository.
- Roadmap: the own model is live since 0.8.0; its class `Trailer` stays unmapped, so trailer boxes are never counted.

## [0.10.1] – 2026-10-23

### Changed

- README brought up to date: own model and its classes, NVIDIA build, IP camera with scene times, how issues and contributions reach the project.

## [0.10.0] – 2026-10-23

First public version.

### Added

- Annotator with an Avalonia window (ADR-0043): file list and counts per class and direction on the left, class and direction quick buttons on the right, playback speed (0.5×–16×), brightness and contrast on top, navigation buttons, a timeline coloured by class with speed marks and a permanent speed input below. Keys and command line stay as before; speeds are stored in `.gt.csv` (an older `.speed.csv` is still read). German and English (default the Windows language), a light/dark switch, both remembered in `%APPDATA%\TrafficCounter\annotator.json`; the entries around the current frame are shown above the counting line in their class colour; the mouse wheel over the video steps one frame back or forward.
- `TrafficCounter.Sync rescale-speed --machine <name>`: only the passages of one counting machine, not those copied in from a second camera by `fill-degraded`.
- Analysis app: footer with copyright, licence (AGPL-3.0) and a link to the source code; the precipitation series name the DWD as source.
- Publication as checked snapshots of a private working repository (`tools/publish/Publish-PublicSnapshot.ps1`, ADR-0045); additional AGPL permission for proprietary runtime libraries (ADR-0044).

### Changed

- Demo data: example bus timetable and example values instead of values taken from the first measurements.
- `select_for_labelling.py` needs `--machine`.

## [0.9.0] – 2026-10-23

### Added

- Camera scene times (ADR-0042): `CameraScenes:Enabled` keeps the day/night scene switch of the IP camera in step with sunrise and sunset. Once a day the service adjusts the current month's entry of the camera's scene schedule (ISAPI `displayParamSwitch`) to sunrise + 30 min and sunset, on the half-hour grid; `CameraScenes:Apply` false only logs what it would set.
- NVIDIA build (`-p:InferenceBackend=Nvidia`, ADR-0041): TensorRT and CUDA execution providers; regression `--nvidia-libs`.
- Targeted labelling: `select_for_labelling.py` (samples where counting and reference camera disagree or the model is unsure) and the keyboard review tool `review_samples.py`; `train_model.py` reads its labels.

## [0.8.0] – 2026-10-22

### Added

- Own detection model (stage 3, ADR-0039): `tools/training/train_model.py` builds a YOLO dataset from Label Studio (hand-counted test recordings left out) and fine-tunes from COCO; optional random pixelation patches (`--pixel-patches`). The detector maps the own classes Van, Agricultural and EScooter.
- Training samples: `SampleCollection:PersonModelPath`, a second model that finds the people to pixelate when the counting model knows none; it runs only on the frames an image is taken from. Without it such a model collects no samples.

### Changed

- App: vans and e-scooters are shown as classes of their own; the bicycle class is labelled "bicycle" only.

### Fixed

- Regression reads enum names such as `"Resize": "Area"` in a site configuration.
- Documentation: the COCO model detects no e-scooters at all (0 of 44 in seven hand counts), not even as bicycles (ADR-0039 addendum).

## [0.7.2] – 2026-10-18

### Added

- Network probe: for IP cameras the service pings the router every 2 s and logs one line per minute with sent, lost, median, 90 % and maximum round trip (`NetworkProbe:Enabled`, `Host`, `IntervalSeconds`), to tell WLAN problems of the counting machine from camera problems. It only measures.

## [0.7.1] – 2026-10-18

### Changed

- Training samples: the space above a bicycle or motorcycle is pixelated only if no rider was detected on it; otherwise only the rider's head is pixelated, so the rider's body stays visible (ADR-0026 addendum, ADR-0039).
- Labelling: ADR-0013, ADR-0015 and ADR-0039 accepted. Label Studio tasks propose the counting model's boxes, with cars, vans, trucks and buses tightened by a stronger model's box of the same vehicle, and show the labelling rules.

### Added

- Regression: `--prelabel <inbox> --model <teacher.onnx>` stores a stronger model's boxes in the training samples (`TeacherBoxes`); `labelstudio_refresh_predictions.py` rebuilds the proposals of unlabelled tasks; the evaluation reports the track confidence of counted vehicles.

## [0.7.0] – 2026-10-18

### Fixed

- Tracker: tracks are removed once their timeout has passed before anything is matched. After the motion gate had skipped the detector for a few seconds, an expired track could be stitched to a different vehicle further along its path and continue as that vehicle.

### Added

- `TrafficCounter.Sync fill-degraded --from --to --source-db … [--dry-run] [--include-missing]`: replaces minutes the counting camera stored as not measured with a second camera's data, only where that camera measured itself; the impaired camera's passages are removed first.
- Pane monitor: disc scale and area configurable per camera (`PaneMonitor:DiscSigma`, `BackgroundSigma`, `MinDiscArea`, `MaxDiscArea`).
- Training samples: entry and exit images at a fixed distance before and after the counting line (`SampleCollection:ViewDistanceShare`); optional night threshold for unsure tracks (`NightDetectionConfidenceBelow`, off by default).
- CameraProbe `monitor`: one CSV line per minute with frames, drops, reconnects and longest gap, to tell camera and network problems apart.
- Regression: `--mine-with <teacher.onnx>` turns passages only a stronger model counted into training samples; enhancement experiment `StaticHighlights` (`--enhance statichighlights`).

### Added

- Analysis app: "Daily pattern" page next to the weekly pattern. For a chosen weekday it shows one row per date (newest first) and one column per hour, so the same day can be compared across weeks.
- Detector option `Resize` (Linear/Area) and enhancement mode `Grayscale`; regression `--resize`, `--enhance grayscale`.

## [0.6.0] – 2026-10-17

### Fixed

- Raindrops on the window pane at night, which hide vehicles behind bright light discs, are detected (`PaneMonitor`); such minutes count as not fully measured ("stream or view impaired") until the view has been clear for 10 minutes (ADR-0024).

## [0.5.1] – 2026-10-16

### Fixed

- Recordings of the service keep only the configured rows of the image (`Recording:BandTop/BandBottom`) but did not say so in their metadata. The regression, sample extraction and speed calibration then placed the full-frame geometry about 150 px too low in the video, cutting off the far lane. The service now writes the band into the metadata, and for older recordings the regression takes it from the configuration snapshot.

### Added

- `TrafficCounter.Regression --extract-samples <class> --recut true` cuts existing samples again in place (same folder names, so labelling tasks keep their images).

## [0.5.0] – 2026-10-16

### Added

- Vehicle class `EScooter` (8) for hand counts (migration `AddEScooterClass`); annotator key `8`, and `b`/`B` jump to the next/previous bicycle entry to reclassify older counts.
- `labelstudio_tasks.py --class <class>` writes tasks for the samples of one class only.

### Changed

- `TrafficCounter.Regression --extract-samples` recognises a passage already cut under another class (by recording and time) and changes the class of that sample instead of cutting it again.

### Fixed

- A camera stream with interruptions or, for an IP camera, clearly fewer frames than its nominal rate no longer counts as measured: such minutes get the new state `Degraded` ("stream interrupted" on the operations page) until the stream has been stable for 10 minutes (ADR-0024). Rates per hour use only the vehicles of measured minutes.

## [0.4.1] – 2026-10-15

### Fixed

- At start-up the service ends only its own machine's unfinished operating periods as unclean, not a period of another machine still running (e.g. a second camera whose data filled a gap).

## [0.4.0] – 2026-10-15

### Added

- `TrafficCounter.Sync delete-passages`, `set-class` and `add-alongside`: correct single counted passages in both databases.

### Fixed

- Trailer coupling: short coupling phases interrupted by a missed detection no longer add up, and a car coupled to the one ahead by mistake is released (and counted) when it falls back.
- With the motion gate, the detector keeps running while a vehicle is at the counting line or has crossed it without being counted yet; a vehicle stopping there could lose its track and its passage.
- The tracker gets the band edges for its "vehicle left the image" check instead of inferring them from the detections; a vehicle hidden while standing in the middle early in a period could lose its track.
- Replaying the fallback file after a database outage continues where it stopped instead of rewriting the rest of the file after every chunk; an interrupted replay writes nothing twice.
- Sync: weather hours are compared per station and hour, so hours backfilled later on one side are carried over.
- Sync correction commands (`rescale-speed`, `mark-unmeasured`, `delete-passages`, `set-class`) change nothing unless both databases are reachable.
- Band-only recordings: a counting line reaching beyond the recorded band is cut along its own direction, and the kerb positions are remapped.

### Changed

- Training samples: anonymising and JPEG encoding run in the background and only for samples that are kept.
- Scheduled recordings open ffmpeg or the video writer on the recording thread, not on the camera's capture thread.
- Sync compares only the keys per table instead of reading every period's rows on both sides.
- Image enhancement copies only the band; tracks cache their typical size.
- Analysis app: the "usual" line on the overview loads only the same weekday of the four weeks before; exports are limited to 366 days.

## [0.3.1] – 2026-10-15

### Fixed

- Prams and the like on the sidewalk, detected as a motorcycle at walking pace, are no longer counted (`Site:SidewalkMotorcycleMaxKmh`, ADR-0020).

### Added

- `TrafficCounter.Sync sync --from --to`: copies only a time range, e.g. to fill an outage of the counting machine with a second camera's data.

## [0.3.0] – 2026-10-15

The counting machine switches to the IP camera (Annke NC400 over RTSP).

### Added

- Evaluation band with a left and right edge (`Site:BandLeft`/`BandRight`, calibration tool key `w`), ADR-0040.
- Camera probe (`probe`, `live`, `record`) and calibration tool work with an IP camera: `--rtsp <url> [--user <name>]`, password only from `TrafficCounter__Source__Rtsp__Password`.
- `TrafficCounter.Regression --extract-samples <class>`: training samples cut from hand-counted recordings.
- Label Studio scripts for labelling the training samples (`tools/training`).
- `TrafficCounter.Regression --count true`: counts a video per interval and direction without a hand count, e.g. to compare two cameras.

### Security

- `System.Security.Cryptography.Pkcs` 10.0.13 instead of 9.0.11 pulled in by the SQL Server client (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7).

## [0.2.1] – 2026-10-11

### Changed

- Training samples: bicycles (including e-scooters) count as a rare class, so more of them are collected.

### Added

- `tools/deploy/Get-TrainingSamples.ps1`: copies new training samples from the counting machine into the private repository.

## [0.2.0] – 2026-10-11

First numbered version after continuous operation started on 2026-10-04. Everything from the start of the project up to then is 0.1.0.

### Added

- Counting service: watchdog for a camera that still delivers frames but only black ones or far too few. Such minutes no longer count as measured, and the camera is reopened ([ADR-0024](docs/adr/0024-operations-log-and-logging.md)).
- `TrafficCounter.Sync mark-unmeasured`: marks a period as not measured in both databases.
- Analysis app (stage 4): overview, period, weekly pattern, speed and operations pages; Excel report and print view; English and German (ADR-0037).
- Reference recordings by the running service on a schedule, straight to the hardware encoder, with frames from the capture thread (ADR-0032).
- Speed estimate with a scale from a measured road marking (ADR-0034); test drives with speedometer readings to validate it (ADR-0035).
- Hourly weather of a DWD station (ADR-0023); PostgreSQL next to SQL Server and the sync tool (ADR-0030).
- Collection of anonymised training samples in operation (ADR-0026).
- Optional image enhancement before detection (CLAHE, gamma), off by default.

### Changed

- Image quality events only after a new level has held for 3 minutes.
- Period table: column "per hour"; values of partly measured hours or days are marked "≈" and explained in a footnote.
- Weekly pattern: seven colour classes with a legend and readable labels.
- Documentation in English (ADR-0036).

### Fixed

- Camera position check: implausible estimates at dusk (scale near 0, "rotations" of 60–94°) no longer raise a false "moved" alarm, and only the very next check confirms a change.
