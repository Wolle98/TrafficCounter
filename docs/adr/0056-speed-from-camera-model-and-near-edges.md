# ADR-0056: Speed on a 3D camera model from the camera-near box edges

- **Status:** accepted
- **Date:** 2026-10-31
- **Stage:** 5
- **Supersedes:** [ADR-0034](0034-speed-scale-from-road-marking.md) for cameras with a camera model (the linear scale between the kerbs stays for cameras without one)
- **Extends:** [ADR-0015](0015-speed-measurement.md), [ADR-0052](0052-standing-vehicles-and-stops.md)

## Context

Up to now the speed was the horizontal image speed of the box bottom centre times a scale. The scale was given at both kerbs and interpolated linearly in pixels per metre (ADR-0034). On the main camera this had four problems:

1. **Lens distortion.** The wide-angle lens bends the image noticeably (division model, k1 ≈ −0.33). The linear scale is about 5 % off in the middle of the road. A global factor of ×1.08, fitted to test drives, covered this up.
2. **Few positions for fast vehicles.** A fit through five or six positions in the zone was up to 10 % off, measured on speed clips (ADR-0053). Raising the minimum alone would have taken the speed from exactly the fast vehicles the measurement is for. At 70 km/h in the near lane, a vehicle is only about 0.36 s in the ±400 px zone, five or six frames at 15 fps. Widening the zone lowered the speeds with the linear scale (−1.5 % at ±500 px, −4 % at ±600 px).
3. **Long vehicles.** A bus fills most of the image width and is cut off at the image border. Its box centre does not move with the vehicle: a city bus measured −24 % against its tracked wheel hubs.
4. **Night.** Headlight glare stretches boxes downwards and puts vehicles of the far lane into the near lane. This is not solved here.

A 3D camera model was fitted from open geodata (orthophoto, cadastre, terrain model) and image points: focal length, distortion, pose and the road surface. It reproduces the road width to 2 cm and a bus wheelbase to +1 %.

Measurements on a one-hour recording with its own references (values in the private repo):

- **Wheel hubs of 48 cars**, tracked frame by frame and put on the plane at hub height. These need no box, no GPS and no speedometer.

  | Method | Against the hubs (median) |
  |---|---|
  | Live counter | −0.6 % |
  | Box centre on the 3D model | −2.3 % |
  | Camera-near edge at the box bottom | +5.9 % |
  | Camera-near edge as a vertical edge up to the box top | 0.0 % |

  The edge-height method stays within ±1.5 % for zones of ±400 to ±800 px.
- **Bus**: wheel hubs 27.9 km/h. Box centre 21.1 km/h; the edge method with height gives 29.0–29.4 km/h for all zone widths, the rest is the mirrors projecting forward.
- **GPS logger**: a hand-held GPS logger was 8–9 % above all video methods and above the speedometer. It is not used as a reference.

The implementation in the counter (`Regression --camera`, 15 fps like the service) gives the same:

| Check | Linear scale (×1.08) | Camera model |
|---|---|---|
| 48 cars against their wheel hubs (median / mean) | −0.6 % / +0.2 % | 0.0 % / +0.9 % |
| City bus, hubs 27.9 km/h | 21.5 km/h | 29.2 km/h |
| 20 speedometer test drives (mean error), linear without ×1.08 | −6.9 % | +1.0 % |
| Fast vehicles of the speed clips (45–69 km/h) | 5 of 13 from only five to seven positions | all 16 with 8–10 positions |

Cars change by +0.9 % (median ratio), vans +2.5 %, trucks +4 %, buses +40 %, bicycles −4 %.

## Options considered

1. Keep the linear scale and only raise the minimum number of positions: fast vehicles lose their speed.
2. Linear scale with a wider zone for fast vehicles: biased by distortion (−1.5 to −4 %).
3. Box centre on the 3D model: no distortion bias, the zone can grow; wrong for long vehicles (−24 % for a bus).
4. **Camera-near box edges on the 3D model**, as vertical edges, with a zone that grows for fast vehicles.

## Decision

Option 4, for cameras with a camera model (`Site.Camera`, `CameraModel` in `TrafficCounter.Counting`, without OpenCV):

- **Lane.** The lane (metres across the road) comes from the box bottom centre in frames where it lies close to the principal point's column. There it is the bottom of the vehicle's near side.
- **Edges.** Left of the principal point the box's left edge belongs to the vehicle's camera-near vertical edge, right of it the right edge, around it both. Each such edge is put into that lane as a vertical edge rising to the roof. The roof's far edge, a class width further across (bus 2.55 m, car 1.8 m, …), shows as the box top. The position along the road is the one whose outermost image column (tilt and distortion included) equals the box edge. Edges within 30 px of the image border are cut off and not used.
- **Fit.** One least-squares line through the positions over the frame timestamps, with a common slope and one offset per edge, so no vehicle length is needed. If fewer than the minimum number of frames have a usable edge, the ground point on the road is used instead.
- **Minimum and zone.** `SpeedMinSamples` = 8 positions. With fewer in the ±0.2 image-width zone, the zone grows in steps of 50 px up to `SpeedMaxZoneFraction` (0.3125 of the width, 600 px at 1920). Fast vehicles keep their speed with enough positions; the method does not drift with the zone width.
- **Unchanged.** The stop rule (ADR-0052) works on metres along the road. Without a camera model the linear scale stays (ADR-0034), with the same minimum and growing zone.
- **No global factor.** The kerb scales of the main camera are no longer used for its speeds, so the ×1.08 factor has no effect on new data.

## Consequences

- Positive: the speed is measured in metres on the road, not by a fitted factor. Long and tall vehicles get correct speeds. Fast vehicles keep theirs, from at least eight positions. Recordings can be re-measured with `Regression --camera`.
- Negative / risks: the camera model must be refitted when the camera is moved (the drift check shows it). The night stretching of boxes still puts far-lane vehicles into the near lane. The class width is assumed; a wrong class changes a bus speed by about 1 %.
- Follow-up work:
  - fit the camera model in the calibrator instead of scripts;
  - a camera model for the second camera;
  - a plausibility check of the lane at night;
  - decide whether stored speeds are recomputed (separate decision, originals kept).
