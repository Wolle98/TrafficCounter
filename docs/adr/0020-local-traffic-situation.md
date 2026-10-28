# ADR-0020: Local traffic situation – parking, evading, waiting, occlusion, cyclists on footpaths

- **Status:** accepted, extended by [ADR-0023](0023-weather-and-image-quality.md) (weather, backlight, image quality), [ADR-0048](0048-slow-e-scooters-on-the-sidewalk.md) (slow e-scooters on the sidewalk) and [ADR-0052](0052-standing-vehicles-and-stops.md) (standing vehicles start over)
- **Date:** 2026-09-26
- **Stage:** 1, 2, 5
- **Extends:** [ADR-0006](0006-object-detection-yolo-onnx.md), [ADR-0008](0008-tracking-bytetrack.md), [ADR-0009](0009-counting-line.md)

## Context

A test photo from the camera position (private repo, `config/reference/`) shows:

- **View perpendicular to the street:** vehicles move horizontally through the image. The near lane runs from left to right, the far lane from right to left.
- **Parking spaces** on both sides: on the left on the near side, on the right on the far side. When they are occupied, vehicles enter the image on the **opposite lane** and cut back in later.
- **Waiting:** vehicles occasionally wait until oncoming traffic has passed, also **on the counting line**. Some of them reverse.
- **Occlusion:** because of the perpendicular view, vehicles on the near lane completely hide those on the far lane as soon as they meet or one of them waits.
- **Cyclists on the footpaths** are to be **counted as well**.
- The road only occupies a **horizontal band** of the image. Above and below are houses, driveways, parked cars with readable number plates and front gardens.

## Decision

1. **Direction only from the movement:** the direction follows exclusively from the side from which a vehicle crosses the counting line ([ADR-0009](0009-counting-line.md)). The lane is never used to determine the direction, because vehicles regularly drive on the opposite lane.
2. **Counting line vertical, roughly in the middle of the image,** from the far property boundary to the near one, i.e. **across both footpaths**. This way cyclists on the footpaths are counted too.
3. **Store the position on the line:** each passage stores where it crossed the line as a value between 0 and 1 (`LinePosition`). Whether it was on the far lane, the near lane or the footpath is only assigned in the analysis, based on the calibrated boundaries. The schema does not fix any zones.
4. **Emitted by distance instead of time** (replaces the rule "N frames or time after the crossing" from ADR-0009):
   - A passage is emitted as soon as the track has covered a minimum distance **beyond the line** (`EmitDistancePixels`), or when the track ends.
   - A vehicle waiting on or directly behind the line is therefore only emitted once it moves on.
   - The timestamp remains the interpolated moment of crossing.
   - If a track ends back on the **side it came from** (reversed), the crossing is discarded.
5. **Tracking across occlusions** (extends [ADR-0008](0008-tracking-bytetrack.md), implemented in stage 1, tracking task):
   - The buffer for lost tracks **depends on the speed**: several seconds for standing or slow tracks, about 1 s for fast ones.
   - **Tracks are stitched:** if a track ends and shortly afterwards a new one starts near the predicted position with a matching direction of motion, both are joined into one track. This way a vehicle hidden in front of the line does not become a "new" vehicle behind the line that would never be counted.
   - Partly hidden vehicles (roof visible above the waiting vehicle) are explicitly part of the test scenarios.
6. **Evaluation area as a band, wide input for the detector** (extends [ADR-0006](0006-object-detection-yolo-onnx.md)):
   - Directly after capture, only a horizontal band around the road and footpaths is processed further. The rest is discarded, in the spirit of [ADR-0005](0005-privacy.md).
   - The band goes to YOLO **as a wide rectangle**, about 1280 × 320, instead of a square 640 image. The model is exported accordingly. This keeps even bicycles on the far side large enough.
7. **Speed (stage 5):** each passage records whether the drive was **unobstructed**: steady movement, no stop in the measuring area. Only unobstructed drives go into speed statistics, such as the mean or the value that 85 % of vehicles do not exceed.
8. **Reference videos** (roadmap 0.7) contain these scenes as separate categories:
   - entering on the opposite lane and cutting back in;
   - waiting in front of the line and on the line;
   - reversing;
   - meeting with occlusion;
   - parking and leaving a parking space;
   - cyclists on the road and on the footpath.

## Consequences

- `CountingSettings.EmitDelay` is dropped in favour of `EmitDistancePixels`. At the end of a track, the counting logic checks whether the vehicle has arrived on the other side of the line.
- New column `VehiclePassage.LinePosition` (migration `AddLinePosition`).
- Pedestrians occasionally push their bicycle along the footpath. They are counted as bicycles; we accept that deliberately.
- The calibration must capture the boundaries of footpath and road along the counting line, so the analysis can distinguish by lane or footpath.

## Implementation note (2026-10-15)

Prams, walkers and pushed bikes on the sidewalk are sometimes detected as motorcycles and counted, about once a day. A motorcycle does not ride at walking pace on the sidewalk, so a passage of class motorcycle outside the kerbs and slower than `Site:SidewalkMotorcycleMaxKmh` (default 10 km/h) is no longer counted. Bicycles on the sidewalk still count, as decided above. Without kerbs or a measured speed the rule does not apply.
