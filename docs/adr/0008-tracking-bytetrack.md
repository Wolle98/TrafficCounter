# ADR-0008: Multi-object tracking with an own ByteTrack implementation

- **Status:** accepted, extended by [ADR-0020](0020-local-traffic-situation.md) (speed-dependent lost buffer, track stitching)
- **Date:** 2026-09-26
- **Stage:** 1

## Context

Counting, direction, class voting and speed need objects with a stable identity across several frames. Single-frame detections are not enough.

## Options considered

1. Centroid/IoU tracker: very simple, but loses identity on occlusion (oncoming traffic).
2. DeepSORT: additionally uses a re-ID network. More effort, and the appearance of people and vehicles would be processed as a feature (privacy).
3. **ByteTrack:** Kalman filter plus two-stage IoU association that also uses low-confidence detections. State of the art for vehicles, without a re-ID network.

## Decision

ByteTrack is implemented in C# in `TrafficCounter.Tracking`, about 400 lines of code. For the association, the Hungarian algorithm (or LAPJV) is implemented in-house to avoid a heavy maths dependency.

## Consequences

- Fully testable with synthetic detections, without a camera.
- Parameters (track thresholds, buffer for lost tracks) are aligned with the frame rate and tuned with the reference videos.
