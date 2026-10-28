# ADR-0009: Counting via a virtual counting line, direction from the crossing direction

- **Status:** accepted, extended by [ADR-0020](0020-local-traffic-situation.md) (emit rule by distance instead of time, line across the footpaths) and [ADR-0046](0046-two-wheelers-and-split-tracks.md) (one vehicle split into two tracks)
- **Date:** 2026-09-26
- **Stage:** 1, 2

## Context

A vehicle should be counted exactly once, even if its track breaks off briefly, and the direction of travel should come at no extra cost.

## Options considered

1. Count every new track: breaks or false detections produce double counts.
2. Counting zone (polygon), count on leaving: robust, but the direction has to be determined separately.
3. **Counting line (segment) across the road**, count when the sign of the reference point relative to the line changes.

## Decision

Option 3.

- The reference point is the centre of the bottom edge of the box (ground contact). It is more stable than the box centre in an oblique perspective.
- A crossing only counts once the point has reached a minimum distance (hysteresis) beyond the line. Each track is counted at most once.
- The direction follows from the sign of the cross product, 1 = A, 2 = B; the names are in the configuration.
- The event is only stored at the **end of the track** or after N frames beyond the line. This way class voting (stage 3) and speed estimation (stage 5) can use the whole trajectory. The timestamp is still the moment of crossing.
- The line and the ROI are set in a still image with `TrafficCounter.Calibrator` and stored as relative values (0..1), so they are independent of the resolution.

## Consequences

- Stage 2 needs hardly any additional code, only configuration and tests.
- Driveways and turning manoeuvres that cross the line twice are deliberately counted only once.
