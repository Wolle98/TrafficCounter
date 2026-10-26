# Reference videos and ground truth

Reference videos are recordings (10–60 min) that are counted by hand once. With them we measure the counting accuracy (acceptance criteria of the roadmap) and safeguard every later change with a regression test. The videos show the site and are kept **exclusively in the private repo** ([ADR-0005](adr/0005-privacy.md)).

## 1. Recording

There are two ways to record:

- **By the running service** on a schedule, without a counting outage ([operations.md](operations.md#reference-recordings-during-operation-adr-0032), [ADR-0032](adr/0032-recordings-by-the-service.md)). This is the usual way; the service's own count of the same time is then in the database (see section 6).
- **With `record`** of the camera probe, while the service is stopped. This is described below.

Set the environment variable to the private repo once:

```powershell
[Environment]::SetEnvironmentVariable("TRAFFICCOUNTER_PRIVATE", "D:\Code\TrafficCounterPrivate", "User")
```

Start the recording, preferably with the same camera settings as found with the camera probe ([camera-probe.md](camera-probe.md)):

```powershell
dotnet run --project src/TrafficCounter.CameraProbe -- record --minutes 15 --band 0.30-0.80 --tags day,dry --exposure -7 --gain 40
```

| Option | Meaning |
|---|---|
| `--minutes` | duration, at most 60 min; end early with `q` in the preview window |
| `--band` | Store only a horizontal band (fractions of the image height from the top). Saves space and leaves out houses and front gardens. For the calibration, record once without `--band` beforehand. |
| `--tags` | categories, see below |
| `--out` | another destination, for example a NAS. Destinations inside the public repo are rejected. |

Result in `recordings/`:
- `<time>.mp4`: the video;
- `<time>.mp4.frames.csv`: timestamp of every frame, so playback has exactly the original timing;
- `<time>.mp4.json`: metadata with start, end, camera settings and tags;
- `<time>.mp4.site.json`: copy of the site configuration at the time of recording. The regression uses this geometry for the video instead of the current one, so recordings remain usable after the camera has been realigned.

**Disk space:** OpenCV writes MPEG-4, which is 1.5–7.5 GB per hour at 1080p, depending on the image content. If ffmpeg is installed (PATH or WinGet), `record` converts the video to H.264 afterwards automatically. It then becomes about 5 times smaller without measurable effect on counting ([ADR-0031](adr/0031-h264-reference-videos.md)). The file name stays the same; the original is only replaced if the number of frames matches. Options: `--compress auto|on|off`, `--encoder libx264|h264_qsv|h264_nvenc`, `--quality 26`. Existing recordings are converted like this (close the annotator first):

```bash
dotnet run --project src/TrafficCounter.CameraProbe -c Release -- compress --video D:\Code\TrafficCounterPrivate\recordings
```

The free Git LFS quota of GitHub (1 GB) still does not last long. Keep raw videos on the NAS or locally ([ADR-0002](adr/0002-repository-split.md)) and only check in the small `.gt.csv` and `.json` files.

## 2. Categories (tags)

Every video gets tags so that the accuracy can be evaluated per situation:

| Area | Tags |
|---|---|
| Light | `day`, `dusk`, `night`, `backlight`, `sunny` |
| Weather | `dry`, `rain`, `snowfall`, `snow-cover`, `wet-road` |
| Pane | `drops`, `fogged` |
| Traffic ([ADR-0020](adr/0020-local-traffic-situation.md)) | `evasion` (evading via the opposite lane), `waiting`, `reversing`, `occlusion`, `parking`, `sidewalk-cyclists` |
| Purpose | `speedtest` (test drives with known speed, section 7) |

## 3. Counting by hand

```powershell
dotnet run --project src/TrafficCounter.Annotator -- D:\Code\TrafficCounterPrivate\recordings\20260928-180000.mp4
```

Without a video the window opens the `recordings` folder of the private repository. The window ([ADR-0043](adr/0043-avalonia-annotator.md)):
- **Left:** the videos of the folder (`✎` already annotated, `▸` subfolders, `..` the parent folder, "Folder…" picks another one); a click saves the current video and opens the other one. Below, the counts of the video per class and direction and the number of speeds.
- **Right:** per class a `←` and a `→` button in the class colour; a click marks a crossing at the current frame with class and direction. Below, "Undo" (like `u`) and "Delete" (like `Del`).
- **Top:** playback speed 0.5× to 16×, brightness and contrast of the image (only for viewing, the video is not changed) with "Reset image"; on the right the language (German, English; default the Windows language) and the switch between light and dark layout. Language and layout are remembered in `%APPDATA%\TrafficCounter\annotator.json`.
- **Bottom:** a timeline with one mark per annotated stretch in the colour of the rarest class there and a small triangle where a speed is stored (a click jumps there), buttons for previous/next entry (with Shift: the previous/next entry that is not a car), ±5 s, ±1 s, ±1 frame and play/pause, the speed input (section 7) and a status line.
- **In the video:** wall clock time, position and frame top left; the entries within a second of the current frame are shown above the counting line in their class colour, the selected one outlined.

A vertical reference line is shown in the middle of the image; `--line 0.45` moves it. It should be roughly where the counting line will be.

All keys work as well:

- When a vehicle's **front wheel** crosses the line: **left arrow** (moving left) or **right arrow** (moving right).
- Directly afterwards, optionally the class: `1` car, `2` van, `3` truck, `4` bus, `5` motorcycle/scooter, `6` bicycle, `7` agricultural (tractor, with trailers as *one* vehicle), `8` e-scooter, `0` unknown, with the digit keys above the letters. On the number pad only `Num4`/`Num6` count (a car); the other number pad keys do nothing, so grazing one after `Num4`/`Num6` cannot change the class. A combination gets exactly one key press, when the towing vehicle crosses the line. For stage 1 the direction is enough; the classes are only needed in stage 3.
- `n` adds a note, for example "occluded" or "footpath". It is typed directly in the video window (Enter saves, Esc cancels); the video pauses meanwhile. `u` undoes the last entry, `Del` deletes the entry closest to the current position (at most 1 s away). If a counting key is held down too long, Windows repeats it; duplicate entries on the same frame are ignored.
- **Reviewing older counts:** before the e-scooter class existed (2026-10-16), e-scooters were counted as bicycles. `b` jumps to the next bicycle entry (`B` to the previous one), 2 s before it at normal speed, and selects it; `8` then turns it into an e-scooter, `6` or another `b` leaves it as it is.
- `v` gives the selected entry a known speed (speedometer reading of a test drive, section 7).
- **Shortcuts for cars:** `Num 4` (moving left) or `Num 6` (moving right) count and set the class car at once. NumLock must be on for this; the digits of the top row remain the class keys.
- Playback: `space` play/pause, `,` `.` or the mouse wheel over the video one frame back/forward, `j` `l` 5 s back/forward, `J` `L` 1 min back/forward, `-` `+` slower/faster (0.5× to 16×; from 4×, intermediate frames are skipped).
- Quit with `q` **twice** (or `Esc`), so that an accidental key press does not end a long session. On the next opening it continues 5 s before the last entry.
- If the tool crashes, nothing is lost, because it saves after every key press. It writes the reason to `annotator-crash.log` in the program directory.

Tip: in dense traffic, work at half speed (`-`).

After every change `<video>.gt.csv` is written. The format is `offset_s;motion;class;note;speed_kmh`, with the time in seconds since the first frame; the speed column is optional and empty for ordinary traffic. An existing file is continued at the next opening.

### Taking it over into the database

```powershell
dotnet run --project src/TrafficCounter.Annotator -c Release -- import D:\Code\TrafficCounterPrivate\recordings\20260929-124955.mp4
```

The counted vehicles go into the database as an operating period of their own, marked as hand count with video name and video position ([ADR-0027](adr/0027-hand-counted-passages.md)). This way the recording fills the gap in which the counting service did not run because of the recording. Importing again replaces the entries of the video. If the recording overlaps a time in which the counter ran, it is rejected; that is always the case for recordings made by the service.

## 4. Evaluating

The library `TrafficCounter.Evaluation` compares the system's counts with the ground truth:
- A count is a hit if it lies within a time tolerance, ±1.5 s by default. This compensates for the different reference points: front wheel when counting by hand, centre of the bottom edge of the box for the counter.
- The result is hits, direction errors, missed and additionally counted vehicles, from which recall, precision, accuracy of the total and of the direction follow, as well as a confusion matrix of the classes.
- Which image direction corresponds to A or B follows from the direction of the counting line: for a line from top to bottom, "moving left" = A.

## 5. Regression run

```powershell
dotnet run --project src/TrafficCounter.Regression -c Release
```

The run plays every reference video with ground truth in `Sequential` mode. Every frame is evaluated and the results are deterministic. For recordings with a band, the site geometry is converted to the stored band automatically.

Output:
- one line per video, with missed and extra passages and the class confusion;
- the computing load per video;
- a summary per category (tag);
- the comparison with the targets of the roadmap. The exit code is 1 if a target is missed.

**Accuracy** (`accuracy`) is `hits / (real passages + additionally counted)`. It therefore drops both from missed and from double or wrongly counted vehicles. Passages with the wrong direction do not count as hits.

| Option | Default |
|---|---|
| `--recordings <folder>` | `%TRAFFICCOUNTER_PRIVATE%\recordings` |
| `--video <video.mp4>` | all videos with ground truth in the folder |
| `--config <site.json>` | `%TRAFFICCOUNTER_CONFIG%` |
| `--model <model.onnx>` | `Detector:ModelPath` from the site file |
| `--provider Auto\|DirectML\|Cpu\|OpenVino` | `Auto` |
| `--max-fps <n>`, `--motion-gate on\|off`, `--threads <n>`, `--openvino-device <d>` | as in the service: every frame, no motion gate ([ADR-0029](adr/0029-computing-load.md)) |
| `--high-confidence`, `--new-track-confidence`, `--two-wheeler-confidence <high>/<new>` | tracker defaults |
| `--tolerance <s>` | `1.5` |
| `--trace <mm:ss-mm:ss>` | with `--video`: prints detections and tracks frame by frame for one section |
| `--calibrate-speed true [--car-length 4.5]` | speed scale from car lengths ([ADR-0033](adr/0033-speed-scale-from-vehicle-lengths.md)) |

To compare with the live service, use the same model and settings as there; the counting notebook uses `yolo11s-640x192`, `--max-fps 15 --motion-gate on`.

After every change to detector, tracking, counting logic or settings, the regression run should be repeated.

## 6. Live comparison (recordings of the running service)

If the service made the recording itself while it was counting ([ADR-0032](adr/0032-recordings-by-the-service.md)), its count for exactly that time is already in the database. The live comparison sets the hand count against this stored count without running the detection again. It thus shows the actual behaviour in operation, with the frame rate and load of the counting machine:

```powershell
dotnet run --project src/TrafficCounter.Regression -c Release -- --live true --database PostgreSql --video <recording.mp4>
```

With `--database SqlServer` (default) after a `TrafficCounter.Sync sync`. Such hand counts are **not** taken over into the database with `Annotator import`: the counter has already counted the same time, so the tool rejects the import ([ADR-0027](adr/0027-hand-counted-passages.md)).

**Live and offline differ if the recording is worse than the live image.** On a backlight recording of 2026-10-06, the service counted 86 % live, the regression on the video only 71–76 %, regardless of model, inference backend (DirectML, OpenVINO) and frame rate. This recording still comes from the service's old recorder: OpenCV MPEG-4, then converted to H.264, i.e. compressed lossily twice. With low-contrast images that costs detections. For such recordings the live comparison applies. Whether the recordings with a hardware encoder ([ADR-0032](adr/0032-recordings-by-the-service.md)) are close enough to the live image is shown by comparing live against offline on the first counted recording of this kind.

## 7. Test drives for the speed ([ADR-0035](adr/0035-test-drives-for-speed-validation.md))

Drives at a known, constant speed check the speed scale. Because normal traffic is on the road, the test drives are not assigned via noted times but marked in the video.

1. **Schedule the recording:** a slot with the tag `speedtest` in the service's `schedule.json` ([operations.md](operations.md)), for example `{ "Start": "2026-10-10T09:00", "Minutes": 60, "Tags": [ "day", "dry", "speedtest" ] }`. The service keeps counting normally.
2. **Drive:** several passes per speed (e.g. 20, 30, 40 km/h), in both directions, with cruise control or a GPS display. Times are not needed. It does help, however, to note the order and speeds.
3. **Mark:** fetch the recording to the PC and mark only the test drives:

   ```powershell
   dotnet run --project src/TrafficCounter.Annotator -c Release -- <recording.mp4> --speed true
   ```

   `--speed true` ticks "Annotate speed" at the bottom left; it can also be ticked in the window. Mark like in a count: left/right arrow at the line (the video pauses), then the class key `0`–`8`; `Num4`/`Num6` or the class buttons mark a car (or the class) in one step. After the class the cursor is in the speed box for the **speedometer reading when crossing the line**, in whole km/h. Enter or "Save" stores it, Esc leaves the mark without a reading, so other traffic can be marked as well without being evaluated. Nothing is prefilled from the previous drive. The box always shows the reading of the selected entry (jump to it with previous/next entry); changing it and saving corrects it, an empty box removes it. A note (`n`, e.g. "braked") flags a disturbed drive; the evaluation summarises such drives separately. The readings are stored in `<recording.mp4>.gt.csv` together with the count; an older separate `<recording.mp4>.speed.csv` of a video without `.gt.csv` is still read and continued. In a normal count, a passage can also get a known speed with `v`.
4. **Evaluate** against the service's measurement (after `TrafficCounter.Sync sync`):

   ```powershell
   dotnet run --project src/TrafficCounter.Regression -c Release -- --speed true --live true --video <recording.mp4>
   ```

   The speedometer reading is converted into the true speed with the speedometer calibration of the test vehicle. The calibration is determined once by comparing with the GPS display on a longer straight road and is entered into the private site file: `"SpeedTest": { "SpeedometerFactor": 0.99, "SpeedometerOffsetKmh": -1.5 }` (true = factor · reading + offset). Without this section the entry counts as the true speed.

   The output shows per drive the speedometer reading, the true and the measured speed with the deviation, as well as the mean error, overall and per direction (drives with a note in a second summary). Without `--live` the detection runs again over the video, for example to check a changed scale.
