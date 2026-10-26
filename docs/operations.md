# Operating the counting service

The counting service `TrafficCounter.exe` (project `src/TrafficCounter.Service`) runs as a console application or as a Windows service ([ADR-0012](adr/0012-runtime-worker-service.md)).

## Prerequisites

1. **Export the model** ([tools/training](../tools/training/README.md)): by default the script creates `models/yolo11s-1280x384.onnx`. For weaker hardware `yolo11s-640x192` is recommended (see below); it is also what the counting notebook uses.
2. **Database:** SQL Server with Windows authentication, or PostgreSQL (see below). The migrations are applied automatically at start-up (`Storage:ApplyMigrations`).
3. **Site configuration** in the private repo, for example `config/site.json` following [`config/site.example.json`](../config/site.example.json). The values for band and counting line come from the calibration tool. The environment variable points to the file:

   ```powershell
   [Environment]::SetEnvironmentVariable("TRAFFICCOUNTER_CONFIG", "D:\Code\TrafficCounterPrivate\config\site.json", "User")
   ```

## Calibrating

With the calibration tool you set the evaluation band, the counting line, the kerbs, the static zones and the ignore zones with the mouse:

```powershell
dotnet run --project src/TrafficCounter.Calibrator -- --device 0
dotnet run --project src/TrafficCounter.Calibrator -- --rtsp rtsp://<ip>:554/Streaming/Channels/101 --user admin
dotnet run --project src/TrafficCounter.Calibrator -- --image D:\Code\TrafficCounterPrivate\config\reference\photo.jpg
```

| Key | Then click |
|---|---|
| `b` | Evaluation band: top edge, then bottom edge. Put the top edge high enough for the vehicle roofs of the far lane to be inside the band. |
| `w` | Band sides: left, then right edge ([ADR-0040](adr/0040-band-sides.md)). Leaves out distorted or disturbed image edges; leave room beside the counting line (the tool warns). The second status line shows the band size and its aspect ratio next to the model's, ideally about the same. `W` restores the full width. |
| `l` | Counting line: start at the opposite property boundary (top), then end at your own (bottom). This puts both footpaths on the line ([ADR-0020](adr/0020-local-traffic-situation.md)). |
| `k` | Kerbs: far, then near kerb, close to the counting line. This makes it possible to distinguish footpath from road later. |
| `z` | Static zone: two corners of a facade, a pole or a kerb edge away from the road ([ADR-0019](adr/0019-camera-position-monitoring.md)). Several are possible; `x` deletes all. |
| `i` | Ignore zone: two corners around the place where parked vehicles stand **with their wheels**, for example a driveway. Detections whose box bottom edge lies there are discarded ([ADR-0025](adr/0025-ignore-zones.md)). The tool rejects zones on the road; if a zone reaches into a footpath, it warns. `u` deletes all. |
| `s` / `q` | save / quit |

- **Direction arrow:** a yellow arrow shows which direction of travel counts as **A**. The direction names are `DirectionAName`/`DirectionBName` in the site file.
- **Storage:** only the geometry is saved, into the file from `TRAFFICCOUNTER_CONFIG`. Other entries of the file are kept. Destinations in the public repo are rejected.
- **Drift reference:** when saving with static zones, the tool also creates `site.drift.json` next to the site file. The file contains feature points of the zones, no image. With it, the service checks every 5 minutes by day whether the camera has moved ([ADR-0019](adr/0019-camera-position-monitoring.md)). This only makes sense if the calibration image comes **from the counting camera itself** (`--device`, `--rtsp` or `--video`), not from a phone photo. `--drift-only true` only renews the drift reference.
- **Check image:** `--render <image.jpg>` draws the saved configuration into a check image without a window. The image shows the site, so this is also only allowed outside the public repo.

## Starting (console)

```powershell
dotnet run --project src/TrafficCounter.Service -- --preview
```

| Option | Effect |
|---|---|
| `--preview` | Debug window with evaluation band, counting line, detections, tracks, counters and fps. `q` quits. Console mode only. |
| `--file <video.mp4>` | Plays a recorded video in real time instead of using the camera. Useful for trying out settings. The passages go into the database. |
| `--TrafficCounter:<key>=<value>` | Overrides any configuration value, for example `--TrafficCounter:Detector:ExecutionProvider=Cpu`. |

## Configuration

Later sources override earlier ones:

1. `appsettings.json` next to the exe: defaults, public.
2. File from `TRAFFICCOUNTER_CONFIG`: site, private.
3. User secrets and environment variables (`TrafficCounter__Detector__DeviceId=1`): secrets and machine-specific settings.
4. Command line.

Relative paths, for example to the model, refer to the folder of the exe. Use absolute paths for service operation.

## IP camera (RTSP)

For an IP camera ([ADR-0017](adr/0017-camera-and-night-operation.md)), in the site file:

```json
"Source": {
  "Type": "Rtsp",
  "Rtsp": { "Url": "rtsp://192.168.1.20:554/h264Preview_01_main", "UserName": "admin", "Fps": 25 }
}
```

The password belongs **in no file in the repo**:
- Console operation: `dotnet user-secrets --project src/TrafficCounter.Service set "TrafficCounter:Source:Rtsp:Password" "<password>"`
- Service operation: system environment variable `TrafficCounter__Source__Rtsp__Password`

The stream runs over TCP by default. If it breaks off, it is reopened automatically. In logs the URL always appears without credentials.

**Tools with an IP camera:** the camera probe and the calibration tool take `--rtsp <url> [--user <name>]` instead of `--device`. The password comes from the same environment variable `TrafficCounter__Source__Rtsp__Password` (a user variable is enough for the tools), never from the command line. A URL with credentials in it is refused.

```powershell
dotnet run --project src/TrafficCounter.CameraProbe -- probe  --rtsp rtsp://<ip>:554/Streaming/Channels/101 --user admin
dotnet run --project src/TrafficCounter.CameraProbe -- live   --rtsp rtsp://<ip>:554/Streaming/Channels/101 --user admin
dotnet run --project src/TrafficCounter.CameraProbe -- record --rtsp rtsp://<ip>:554/Streaming/Channels/101 --user admin --minutes 30 --band 0.22-0.97
dotnet run --project src/TrafficCounter.Calibrator  --       --rtsp rtsp://<ip>:554/Streaming/Channels/101 --user admin
```

- `probe` measures resolution, real frame rate, gaps and reconnects over 15 s; there are no controls to test, they are set in the camera's web interface.
- `live` shows the stream with frame rate, brightness, sharpness and horizontal guides for mounting the camera level.
- The calibration tool takes a frame from the stream; with `--drift-only true` it only renews the drift reference.

**Setting up a Hikvision-based camera** (e.g. Annke NC400), in its web interface:

1. Set a password, then enable RTSP and, if wanted, ONVIF (*Network > Advanced > Integration protocol*). The main stream is `/Streaming/Channels/101`, the sub stream `/102`.
2. *Video*: main stream H.264 (not H.265, which needs more decoding work), 25 fps, constant bitrate, the highest resolution.
3. *Image*: switch off the white light and infrared, because behind a pane they only light up the glass. If the camera hangs upside down, rotate the image by 180° there (often *Mirror: Center* or *Rotate*), not in the counting software.
4. *Exposure*: a fixed maximum shutter time of about 1/250 s or shorter, so that vehicles are not smeared; the camera then raises the gain at night instead.
5. Lens hood up to the pane, so that neither the room nor the camera's own stand reflects in the glass ([ADR-0023](adr/0023-weather-and-image-quality.md)).
6. Check with `probe` and `live`, then calibrate again, including the drift reference and the speed scale ([ADR-0034](adr/0034-speed-scale-from-road-marking.md)).

## Logs and operations log ([ADR-0024](adr/0024-operations-log-and-logging.md))

- **Log files:** in console mode under `logs/` next to the exe, as a service under `%ProgramData%\TrafficCounter\logs`. Daily rotation, 14 days retention, level `Debug`.
  - Every passage appears with track ID, direction, class, position on the line and number of frames.
  - Every minute a summary appears: frames, dropped frames, detections, passages, fps and detection time per frame.
- **Windows event log:** warnings and errors, only in service operation. The source is `TrafficCounter`.
- **Database:**
  - `OperatingPeriod`: start, end and end reason.
  - `MinuteStatistic`: one row per minute.
  - `SystemEvent`: camera connected or lost, database unreachable, errors.
- **Fallback file:** if the database is unreachable, the data goes into `fallback/pending-records.jsonl` in the same base folder as the logs. It is loaded automatically as soon as the database is reachable again.

## Computing load and weaker hardware ([ADR-0029](adr/0029-computing-load.md))

The defaults aim at the highest accuracy: every frame at 30 fps goes through the model `yolo11s` at 1280×384. For a notebook with integrated graphics or a small computer there are six knobs. They can be combined freely and multiply:

| # | Knob | Setting | Effect |
|---|---|---|---|
| 1 | Processing frame rate | `Processing:MaxFps`, e.g. `10` or `15` (0 = every frame) | frames in between are discarded |
| 2 | Model input size | export with `--width 640 --height 192`, then `Detector:ModelPath`; or a dynamic model (`--dynamic`) with `Detector:InputWidth`/`InputHeight` | 640×192 is a quarter of the computation of 1280×384 |
| 3 | Motion detection | `Processing:MotionGate:Enabled` = `true` | the model only runs when something moves in the band (except in ignore zones), 1.5 s afterwards and at least every 5 s |
| 4 | Model variant | export with `--model yolo11n.pt` | about a third of the computation of `yolo11s`, detects small and rare objects less well |
| 5 | Intel runtime and INT8 | build with `-p:InferenceBackend=OpenVino`, `Detector:ExecutionProvider` = `OpenVino`, `Detector:OpenVinoDevice` = `GPU`/`NPU`/`CPU`/`AUTO`; INT8 with `tools/training/quantize_model.py` | uses Intel graphics or NPU; INT8 about twice as fast |
| 6 | Camera frame rate | `Source:Usb:Fps`, e.g. `15` | saves decoding the frames; the camera must support the rate in 1080p (check with `probe`). The UGREEN webcam only does 1080p at 20/30 fps and falls back to 720p at 15 fps; there it stays at 30 and `MaxFps` limits the processing. |

**Image enhancement at night** (`Processing:Enhancement`, off by default): `Mode` = `Clahe` (local contrast enhancement) or `Gamma` (brightening), `From` = `Night` (default), `Dusk` or `Day`, plus `ClipLimit`/`Tiles` or `Gamma`. The enhanced copy is only used for detection; recordings and samples keep the original. Measured on a heavy-rain night recording: CLAHE raised the offline accuracy from 14 % to 23 % (28 % together with night thresholds 0.1/0.15), dry nights were unchanged; not worth enabling yet. The regression tests it with `--enhance clahe[:clip]|gamma[:value]`, `--enhance-from` and `--night-confidence high/new`.

Further settings:
- `Detector:IntraOpThreads` limits the CPU threads of the inference. 0 means all cores.
- The minute log shows on which share of the frames the model ran and how long a run took.

**Measured on a notebook with i5-1334U and Iris Xe graphics**, 15 fps, motion detection, OpenVINO on the graphics, 478 hand-counted vehicles:

| Model | Accuracy overall | of which night | ms per run |
|---|---|---|---|
| **`yolo11s-640x192`** (recommended) | **94.0 %** | **90.6 %** | 24 |
| `yolo11s-1280x384` | 93.2 % | 78.1 % | 56 |
| `yolo11n-640x192` | 88.1 % | 43.8 % | 20 |

On the Iris Xe graphics, INT8 was slower at 27 ms than normal precision. OpenVINO computes in half precision there anyway; INT8 pays off rather on the CPU.

**Trying it on the target hardware:** the regression takes the same settings and reports the computing load next to the accuracy, including the detector time per second of video. Below 1000 ms the hardware keeps up in real time:

```powershell
dotnet run --project src/TrafficCounter.Regression -c Release -- --model models/yolo11s-640x192.onnx --max-fps 10 --motion-gate on --provider Cpu --threads 4
```

For OpenVINO the build switch is given with every `dotnet run`/`build`/`publish`, e.g. `dotnet run -p:InferenceBackend=OpenVino --project …`. Both runtimes bring their own `onnxruntime.dll`, which is why they only exist as separate builds.

## PostgreSQL on the NAS ([ADR-0030](adr/0030-postgresql-and-synchronisation.md))

On the development machine the counter writes into SQL Server Express. For unattended operation, for example on the notebook, it can write into PostgreSQL on the NAS. `TrafficCounter.Sync` synchronises both databases.

**One-time setup.** You set the passwords as environment variables in the current PowerShell, not in commands or files:

```powershell
$env:PGPASSWORD = Read-Host 'Password pgadmin'
$env:TRAFFICCOUNTER_PG_PASSWORD = Read-Host 'New password for the role trafficcounter'
dotnet run --project src/TrafficCounter.Sync -- check-postgres --host <NAS> --port 5432 --admin pgadmin   # only checks the login
dotnet run --project src/TrafficCounter.Sync -- setup-postgres --host <NAS> --port 5432 --admin pgadmin
```

This creates the role `trafficcounter` and the database `trafficcounter` and applies the migrations. Then you set the connection permanently as user variables, the password separately:

```powershell
setx TRAFFICCOUNTER_PG "Host=<NAS>;Port=5432;Database=trafficcounter;Username=trafficcounter"
setx TRAFFICCOUNTER_PG_PASSWORD "<password of the role>"
```

**Main database of a machine:** `TRAFFICCOUNTER_DB_PROVIDER` (`SqlServer` or `PostgreSql`, default `SqlServer`) chooses the database the analysis app and the tools use when nothing else is given: Regression `--live`, Annotator `import`, Sync `weather`. The counting service keeps its own setting (`TrafficCounter:Storage:Provider`). With `setx TRAFFICCOUNTER_DB_PROVIDER PostgreSql` the development machine works on the NAS directly; SQL Server then only receives data with a sync.

**Synology:** the PostgreSQL package from the Package Center runs next to the built-in DSM database server. It therefore uses a port of its own, here 5433, and its own configuration files in the package's data directory (`pg_hba.conf`). Do not use port 5432 of the DSM server.

**Synchronise** as often as you like, for example after work on the development machine or before analysing there:

```powershell
dotnet run --project src/TrafficCounter.Sync -- sync                      # both directions
dotnet run --project src/TrafficCounter.Sync -- sync --direction to-postgres
```

- Missing rows are transferred. Of running or finished periods, the newer state wins. Hourly weather is copied along.
- **Second camera:** if another camera counts the same street into a database of its own, `TrafficCounter.Sync fill-degraded` fills the minutes the counting camera stored as not fully measured with its counts. `tools/deploy/Sync-FillDegraded.ps1` runs sync, fill and sync again over the last 48 hours, e.g. hourly as a scheduled task; `-NotBefore` keeps it away from times the two cameras counted with different models. Its log is `%LOCALAPPDATA%\TrafficCounter\sync-fill-degraded.log`.
- Pending migrations are applied to both databases first.
- Hand-counted recordings are replaced on the other side after a new import.
- **Deletions are not transferred.**

**Rescaling stored speeds** after a new speed calibration: `TrafficCounter.Sync rescale-speed --from <time> --to <time> --old <far>/<near> --new <far>/<near> --far-kerb <pos> --near-kerb <pos> [--dry-run true]` (kerb positions as fractions along the counting line). Run it on both databases, since changes are not synchronised.

**Filling a gap with a second camera:** `TrafficCounter.Sync sync --direction to-postgres --from <time> --to <time>` copies only the rows in that range (passages, minutes, events, drift measurements) and only the operating periods overlapping it. With `TRAFFICCOUNTER_DB` pointing to the database of a second counter, e.g. a camera that counted the same street meanwhile, it fills an outage of the counting machine. Never copy the rest of that database afterwards: the overlapping times would count twice.

**Correcting single passages** found wrong on review: `TrafficCounter.Sync delete-passages --sync-ids <id>,...` (e.g. a pram counted as a motorcycle), `set-class --sync-ids <id> --class Bicycle` (wrong class) and `add-alongside --sync-id <id> [--class Bicycle]` (a second vehicle the counter merged into this passage). They change both databases, because the sync only copies missing rows; `--dry-run true` shows what would change.

**Marking a period as not measured**, e.g. while a camera delivered only black frames before the watchdog existed: `TrafficCounter.Sync mark-unmeasured --from <time> --to <time> [--dry-run true]`. It sets the minutes to "source unavailable" in both databases, so the analysis treats them as a gap instead of as measured time without traffic.

**Stalled camera:** the service watches whether the camera still delivers a usable picture (`TrafficCounter:SourceWatchdog`). If a whole window (default 120 s) has only black frames, or far fewer frames than normal, it logs an error, stores a `SourceLost` event, marks the minutes as not measured and reopens the camera, again after every window until the picture is back.

**Interrupted or slow stream:** if the camera delivers no frames for 5 s and is reconnected, or an IP camera delivers less than 90 % of its nominal rate (`TrafficCounter:Source:Rtsp:Fps`) over a window, the minutes count as "stream interrupted" (`Degraded`) until the stream has been stable for five windows (10 minutes; `TrafficCounter:SourceWatchdog:MinFrameShare`, `DegradedHoldWindows`). The operations page shows them as a separate column; they count as not measured, and the overview extrapolates them. Such minutes still contain counted vehicles, but too few (ADR-0024).

**Raindrops on the pane:** at dusk and night the service counts drop-like light discs every 10 s (`TrafficCounter:PaneMonitor`). If the median of a 2-minute window reaches `MinDiscs` (default 35; dry nights 10–20, rain 70–80), the minutes count as "stream or view impaired" until the view has been clear for five windows (`ClearWindows`). The debug log has the median of every window ("Pane check: …") for tuning; `Enabled: false` switches the check off (ADR-0024).

**Switching the service to PostgreSQL:** `TrafficCounter:Storage:Provider` = `PostgreSql` and the connection `ConnectionStrings:TrafficCounter` with password, preferably as an environment variable or user secret, never in a file in the repo. The installation script for the notebook does this with `-DatabaseProvider PostgreSql`.

**Hand counts directly into PostgreSQL:** `dotnet run --project src/TrafficCounter.Annotator -- import --provider PostgreSql <video.mp4>`.

## Analysis app ([ADR-0014](adr/0014-gui-technology.md))

`TrafficCounter.App` shows the counted data in the browser: overview for today and yesterday, periods with quick ranges, CSV and Excel export and a print view, weekly pattern, daily pattern (one weekday across weeks), speed and operations. It uses the same database settings as the service:

```powershell
dotnet run --project src/TrafficCounter.App -c Release
```

Then open `http://localhost:5080`. For the database on the NAS:

```powershell
$env:Storage__Provider = 'PostgreSql'
$env:ConnectionStrings__TrafficCounter = 'Host=<nas>;Port=5433;Database=trafficcounter;Username=trafficcounter'
# password from TRAFFICCOUNTER_PG_PASSWORD
dotnet run --project src/TrafficCounter.App -c Release
```

**Languages:** English and German, chosen at the top right of every page; without a choice the browser's language applies ([ADR-0037](adr/0037-multilingual-analysis-app.md)). The texts are in `src/TrafficCounter.App/Resources/`: `Strings.resx` (English) and one `Strings.<language>.resx` per translation. **Adding a language:** copy `Strings.resx` to e.g. `Strings.fr.resx`, translate the values (keep the placeholders `{0}`, `{1}`; the `Format.*` entries are date formats of that language), rebuild. The app offers the new language automatically; the tests report missing texts or placeholders.

The app becomes reachable in the home network with `--urls http://0.0.0.0:5080`. That needs a rule in the Windows firewall. There is no login; the app therefore does not belong on the internet.

**Permanently on the counting machine:** `tools\deploy\Publish-TrafficCounterApp.ps1` builds a package (`dist\TrafficCounterApp`). On the target machine, `Install-TrafficCounterApp.ps1 -OpenFirewall` sets it up as administrator. The script:

- installs the app as the service "TrafficCounterApp" on port 5080;
- takes the database settings of the counting service;
- opens the firewall only for the private network profile.

Afterwards the app is reachable in the home network at `http://<computer name>:5080`, also from the phone.

## Weather data ([ADR-0023](adr/0023-weather-and-image-quality.md))

Hourly weather data of the nearest DWD station (precipitation, temperature, sunshine, wind, cloud cover, visibility) comes via Bright Sky (open DWD data, no API key) into the table `WeatherHour`. Only the station ID leaves the machine, no coordinates. The ID is in the private site configuration:

```json
"Weather": { "StationId": "01234" }
```

- **Running service:** imports the last 48 hours every hour. The DWD adds and corrects values afterwards.
- **Catching up:** `TrafficCounter.Sync weather [--provider PostgreSql] [--from 2026-10-01] [--to …]`.
- **Synchronisation:** `sync` copies the weather hours between the databases.
- **Analysis app:** shows precipitation below the traffic charts and compares traffic in rain and dry weather on the "Period" page.

## Reference recordings during operation ([ADR-0032](adr/0032-recordings-by-the-service.md))

The service can record reference videos itself without interrupting counting. To do so, enable it once in the private configuration (then restart the service):

```json
"Recording": {
  "Enabled": true,
  "Directory": "C:/TrafficCounter/recordings",
  "FfmpegPath": "C:/Users/<name>/AppData/Local/Microsoft/WinGet/Links/ffmpeg.exe",
  "Encoder": "h264_qsv",
  "BandTop": 0.22,
  "BandBottom": 0.97
}
```

The time slots are in `schedule.json` in the recording folder. The service reads the file every 30 seconds; no restart is needed for that:

```json
[
  { "Start": "2026-10-06T16:00", "Minutes": 60, "Tags": [ "day", "rain" ] },
  { "Start": "2026-10-06T17:00", "Minutes": 60, "Tags": [ "dusk", "rain" ] }
]
```

- Each slot lasts at most 60 minutes.
- The file name is the start time (`yyyyMMdd-HHmmss.mp4`), plus the same companion files as with `record`.
- `BandTop`/`BandBottom` record only a horizontal band of the image (fractions of the image height), which saves space and leaves out houses and front gardens.
- With a hardware encoder (`h264_qsv` for Intel Quick Sync, the default, or `h264_nvenc`), ffmpeg gets the frames directly during the recording and writes H.264 right away. The graphics unit encodes, the processor only copies. There is no intermediate file and no later conversion.
- With `libx264` (processor) the service writes Motion JPEG into an `.avi` during the slot. This costs much less computing time per frame than MPEG-4, and the recording loses no frames even with noisy dusk images. Afterwards ffmpeg converts it into the `.mp4` (H.264) with low priority and deletes the `.avi`. That is temporarily about 3 GB per hour of recording.
- If the service stops during a recording, the video written so far is finished properly, and after a restart within the slot a new file starts. A leftover `.avi` can be converted later with `CameraProbe compress --video <folder>`.
- `Encoder: "none"` records MPEG-4 without conversion.
- The service runs as "Local System" and does not find an ffmpeg installed via WinGet by itself, hence `FfmpegPath`.
- The recorder takes the frames straight from the camera's capture thread, not from the counting loop. A slow detector run therefore no longer leaves gaps in the video; the video has the camera's full frame rate, even where the counting skipped frames.
- While a recording is running, the log contains a warning with `RECORDING`.

## Collecting training samples ([ADR-0026](adr/0026-targeted-collection-of-training-samples.md))

For the own model (stage 3), the service can collect images of vehicles that the current model recognises uncertainly, rarely or late. In addition there is a random sample of 2 %. The mode is off by default and needs an end date (at most 30 days ahead):

```json
"SampleCollection": {
  "Until": "2026-11-05T00:00:00+01:00",
  "Directory": "C:/TrafficCounter/datasets/inbox",
  "DailyLimit": 300
}
```

- **Per vehicle** there are three images of the evaluation band: when first seen, at the counting line and when last seen. In addition `sample.json` with trigger, class, direction and the boxes as labelling proposal.
- **Before storing**, the following is pixelated:
  - the lower third of every vehicle box (number plates);
  - the upper part of every person box (faces);
  - the head area above bicycles and motorcycles;
  - the ignore zones completely.

  Unpixelated images never reach the disk.
- **Limits:**
  - at most 200 vehicles per day by default (`DailyLimit`), spread over the day: at any time of day, 10 % (`BurstShare`) plus the elapsed part of the day is available. A busy morning thus does not use up the quota for evening, dusk and night;
  - day folders older than 90 days are deleted (`RetentionDays`);
  - the trigger thresholds are in the configuration.
- **Log:** at start and end the service logs the mode; the operations log contains an event.
- **Triggers:** class wavers, unsure detection (not at night), rare class (truck, bus, motorcycle, agricultural, bicycle/e-scooter), slow, recognised late, not counted although tracked, random sample.
- **Further processing:** `tools/deploy/Get-TrainingSamples.ps1 -ComputerName <ssh host>` copies the new samples from the counting machine into the private repo (`datasets/inbox`, images in Git LFS); they are labelled there. It skips samples already copied and can run any time.

## Installing on another computer (notebook)

The scripts in `tools/deploy/` build a self-contained package that needs no .NET on the target machine, and set up the Windows service there.

1. **On the development machine**, package. Export the models beforehand with `tools/training/export_model.py`:
   ```powershell
   .\tools\deploy\Publish-TrafficCounter.ps1 -Backend OpenVino -Models yolo11s-640x192.onnx, yolo11s-640x192-int8.onnx
   ```
   The result is in `dist\TrafficCounter-OpenVino`. `-Backend DirectML` fits machines without Intel graphics. The package contains `appsettings.Production.json` with notebook values: 15 fps, motion detection on, 4 threads.
2. **Copy** the package folder **to the target machine**. The private site file `site.json` with `site.drift.json` comes from the private repo and does **not** belong in the package.
3. **On the target machine**, install in *Windows PowerShell as administrator*:
   ```powershell
   .\Install-TrafficCounter.ps1 -SiteConfig C:\path\to\site.json -Model yolo11s-640x192.onnx -DisableSleepOnAc
   ```
   The script
   - copies to `C:\Program Files\TrafficCounter`;
   - sets the system variables `TRAFFICCOUNTER_CONFIG`, `ConnectionStrings__TrafficCounter` and `TrafficCounter__Detector__ModelPath`;
   - gives the service account access to SQL Server;
   - creates the service, with delayed automatic start and restart after a crash;
   - starts it and shows the last log lines.
4. **Choosing the camera:** if the machine has a built-in webcam, it is usually device 0 and the counting camera device 1. The number can change, however, when a camera is plugged in or out. It is better to select the camera by part of its name (`Source:Usb:DeviceName`, e.g. `"UGREEN"`); then `DeviceIndex` is ignored. `TrafficCounter.CameraProbe list --names-only true` shows the names without opening a camera and therefore also works while the service runs. The setting belongs in the machine's site file, not in the program files, so that a reinstallation does not overwrite it. A virus scanner with webcam protection (e.g. Bitdefender) must allow the service's access. `Source:Name` (e.g. `"NC400"`) names the camera in every operations event, so the events of two cameras counting the same street can be told apart; without it the USB device name or the source description is used.
5. **Check:** the log under `%ProgramData%\TrafficCounter\logs` must contain `USB camera ... opened` and minute lines. If "Local System" cannot open the camera, reinstall with `-Account "COMPUTER\User"`; the script then asks for the password.

**Updating** an installed service: stop it, copy the new `TrafficCounter.*` files of a publish over the installation, start it again. The counting pauses for about 20 seconds; check `schedule.json` beforehand so as not to cut a scheduled recording.

`Uninstall-TrafficCounter.ps1` removes the service. Database, logs and site file are kept.

## Installing as a Windows service (manually)

In a PowerShell with administrator rights:

```powershell
dotnet publish src/TrafficCounter.Service -c Release -o C:\Program\TrafficCounter
sc.exe create TrafficCounter binPath= "C:\Program\TrafficCounter\TrafficCounter.exe" start= delayed-auto depend= MSSQLSERVER
sc.exe failure TrafficCounter reset= 86400 actions= restart/60000/restart/60000/restart/300000
sc.exe start TrafficCounter
```

- **Dependency on SQL Server:** `depend= MSSQLSERVER` makes SQL Server start first. In addition, the service waits anyway until the database is reachable.
- **Automatic restart:** the recovery options restart the service after a crash. The service exits with exit code 1 on severe errors.
- **Service account:** "Local System" by default. The environment variable `TRAFFICCOUNTER_CONFIG` must then be set as a **system variable**, not as a user variable. User secrets only apply to your own user account.
- **No preview window:** in service operation the preview window is switched off.
