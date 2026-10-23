# ADR-0025: Ignore zones for parked vehicles

- **Status:** accepted
- **Date:** 2026-09-29
- **Stage:** 1

## Context

The evaluation band must reach high enough at the top for buses and trucks on the far lane to be completely in the image ([ADR-0020](0020-local-traffic-situation.md)). As a result, driveways and parking spaces beyond the footpaths also lie in the band. Vehicles parked there are constantly detected and tracked.

They are not counted, because they do not cross the counting line. But they have three disadvantages:

- If a vehicle on the far lane passes in front of a parked one, the boxes overlap. The tracker can then swap tracks, and the class-agnostic NMS can discard one of the two boxes.
- The number of detections in the minute statistics is inflated and loses its meaning.
- The tracks are picked up again after every occlusion, and the preview becomes cluttered.

## Options considered

1. **Shrink the evaluation band at the top.** This cuts off tall vehicles on the far lane and worsens their detection and classification.
2. **Ignore standing tracks.** This also hits vehicles waiting on the line ([ADR-0020](0020-local-traffic-situation.md)). It does not help against swaps when passing.
3. **Ignore zones by ground point.** The operator marks rectangles around the places where the wheels of parked vehicles stand. What counts is the ground point, i.e. the centre of the bottom edge of the box, as in counting.

## Decision

We choose option 3.

- The site file gets `IgnoreZones`: rectangles as fractions of the image size. They are drawn in the calibrator with `i` and deleted with `u`.
- A detection whose ground point lies in a zone is discarded **after detection and before tracking**. It does not count as a detection and creates no track.
- A zone must not overlap the road between the kerbs. `SiteOptions.Validate` rejects such zones. If a zone reaches into the rows of the footpaths, the calibrator warns, because cyclists would be missed there.
- A vehicle leaving a driveway is tracked normally from the footpath on and counted as soon as it crosses the line.
- The preview shows the zones hatched in grey.

## Consequences

- Positive: parked vehicles no longer disturb tracking and statistics. The geometry stays relative to the image size, like band and line.
- Negative / risks: the NMS in the detector runs before the filter. If a passing vehicle overlaps a parked one very strongly, it can still discard one of the two boxes; with the perspective here, however, the boxes usually stay well below the NMS threshold. The zones are rectangles; an oblique driveway is only roughly covered. The check against the road assumes a roughly horizontal street. If the camera moves, the zones do not move along ([ADR-0019](0019-camera-position-monitoring.md) detects this; a recalibration is then needed).
- Follow-up work: none.
