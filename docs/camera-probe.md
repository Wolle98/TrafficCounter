# Camera probe and night test

The tool `TrafficCounter.CameraProbe` checks whether a USB camera meets the requirements of [ADR-0017](adr/0017-camera-and-night-operation.md). Except in the explicit recording mode (`record`), it stores **no images** ([ADR-0005](adr/0005-privacy.md)).

## Starting

```powershell
dotnet run --project src/TrafficCounter.CameraProbe -- list
dotnet run --project src/TrafficCounter.CameraProbe -- probe --device 0 --json probe-day.json
dotnet run --project src/TrafficCounter.CameraProbe -- live --device 0
```

The camera must not be open in any other application (Teams, Zoom, camera app), and the counting service must be stopped. The output of `probe --json` contains only technical data. I would still keep it in the private repo under `recordings/camera/`, together with the notes on the night test.

| Command | Purpose |
|---|---|
| `list` | Shows the cameras Windows knows and the device indices OpenCV can open. `--names-only true` only lists the DirectShow names without opening a camera, so it also works while the service runs. |
| `probe` | Automatic test, takes about 1 minute. It checks formats and measured fps, which settings can be changed and whether manual exposure, gain and focus actually change the image. At the end there is an assessment according to ADR-0017. |
| `live` | Live image with measurements; settings can be changed by key. For alignment and the night test. |
| `record` | Recording of reference videos: only explicitly, limited in time, never into the public repo. See [reference-videos.md](reference-videos.md). |
| `compress` | Converts existing recordings to H.264 ([ADR-0031](adr/0031-h264-reference-videos.md)). |

Options: `--device`, `--device-name <part of the name>` (instead of the index), `--backend dshow|msmf` (default `dshow`), `--width`/`--height`/`--fps`/`--fourcc` (default 1920×1080, 30, MJPG). With `--exposure`, `--gain` and `--focus` you set start values; automatic exposure or autofocus is switched off in the process.

**IP camera:** `probe`, `live` and `record` also take `--rtsp <url> [--user <name>] [--fps 25]` instead of the device options, with the password in the environment variable `TrafficCounter__Source__Rtsp__Password`. `probe` then only measures what the stream delivers (resolution, real frame rate, gaps, reconnects), and `live` has no control keys; the camera is set up in its web interface ([operations](operations.md#ip-camera-rtsp)).

## Live view

- **Line 1:** resolution, format, **measured fps**, age of the frame, dropped frames, reconnects.
- **Line 2:** exposure (raw value and time, e.g. `-7 ≈ 1/128 s`), automatic exposure, gain, focus, autofocus.
- **Line 3:** **brightness** (0–255) and **sharpness**. Sharpness drops with blur from wrong focus and with motion blur; for comparison, not as an absolute value.
- Yellow **guide lines** (key `h`) for horizontal alignment: the kerbs should run parallel to the lines.

| Key | Effect |
|---|---|
| `a` | automatic exposure on/off |
| `e` / `E` | exposure shorter / longer (one step = factor 2) |
| `g` / `G` | gain −10 / +10 |
| `f` | autofocus on/off |
| `[` / `]` | focus −5 / +5 |
| `s` | driver settings dialog (DirectShow only) |
| `p` | write the current settings as JSON to the console (for the later configuration) |
| `q` / Esc | quit |

## Procedure

### 1. In daylight

1. Mount the camera, start `live` and align it with the guide lines: the road and both footpaths should be in the middle of the image, the kerbs horizontal.
2. Run `probe --json probe-day.json`. While it runs, keep movement in the image to a minimum.
3. Look at the assessment:
   - `1920x1080 at ≥ 20 fps`
   - `Autofocus can be switched off`
   - `Manual exposure changes the image`
   - `Exposure can be limited to ≤ 1/100 s`

   `FAILED` means the camera is unsuitable. `UNCLEAR` means it has to be checked by hand in `live`.

### 2. At night (decision point, roadmap 0.6)

1. Start `live`, switch autofocus off with `f` and set the focus with `[`/`]` so that sharpness is at its maximum, ideally on a road marking.
2. Switch automatic exposure off with `a` and set the exposure with `e`/`E` to **−7 (≈ 1/128 s)**; if necessary −6 (≈ 1/64 s).
3. Increase the gain with `G` until vehicles are recognisable. Noise is fine.
4. Let some vehicles pass and observe:
   - Are the outlines of vehicles, cyclists and scooters recognisable?
   - How strong is the motion blur? How bright are headlights?
   - Does the fps display stay above **20**?
   - Is anything from the room reflected in the pane?
5. Output the settings with `p` and note them together with the observations.

**Criteria from ADR-0017:** the camera stays if
- the focus stays stable,
- exposure can be limited to ≤ 1/100 s without the image becoming useless,
- the frame rate does not drop below 20 fps at night,
- vehicles are recognisable in the image.

Otherwise the switch to an IP camera follows. A problem with real images or site details is recorded as an issue in the **private** repo ([ADR-0022](adr/0022-issue-tracking.md)).

## Notes

- **Exposure values:** UVC cameras usually report the exposure as log2 of the exposure time in seconds, for example −7 ≈ 1/128 s. Some drivers use other units. Then the live view only shows the raw value, and the effect shows in the image.
- **Settings get lost:** some cameras reset settings when unplugged. The counting service therefore sets them again every time it opens the camera.
- **Media Foundation:** with `--backend msmf` you can compare whether Media Foundation supports more settings than DirectShow.
