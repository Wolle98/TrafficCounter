# ADR-0019: Monitoring the camera position and drift compensation

- **Status:** accepted
- **Date:** 2026-09-26
- **Stage:** 1 (detection), 2 (compensation), 5 (mandatory)

## Context

The camera can move slightly: by being bumped, through temperature changes, a yielding mount or, with a webcam, simply by slipping. Then the counting line, the ROI and later the homography for the speed ([ADR-0015](0015-speed-measurement.md)) no longer match the street.

- For counting, the hysteresis absorbs small shifts: the line with a 10 % margin, see [ADR-0009](0009-counting-line.md).
- For speed measurement, however, even a few pixels lead to systematic errors.

## Options considered

1. Only tolerance in the design: enough for stage 1 but not for stage 5, and a shift would go unnoticed.
2. Comparison with a stored reference image: robust, but we would have to store an image of the scene permanently (privacy).
3. **Comparison of distinctive feature points in static image areas:** only the points themselves are stored, no image.
4. Plausibility check via the lanes derived from the tracks: independent of the image content, but slow to react.

## Decision

Option 3 as the main method, option 4 later as an additional check.

- **Reference:** during calibration, ORB feature points with their descriptors are stored in **static zones**, i.e. house walls, kerbs, poles. The road is left out because vehicles move there. The zones are set in the calibration tool. **No image** is stored, see [ADR-0005](0005-privacy.md).
- **Check:** about every 5 minutes the system picks a frame without active tracks. It matches the feature points and estimates shift, rotation and scale from them (`Cv2.EstimateAffinePartial2D` with RANSAC). The quality of the match is the share of matching points.
- **Reaction:**
  - deviation below the tolerance: do nothing, only log;
  - **small drift:** line, ROI and homography are moved along with the estimated transformation (from stage 2);
  - **large deviation or uncertain match:** warning in the log and Windows event log; affected passages and operating periods are marked as "calibration doubtful".
- **At night** the match is unreliable because the scene looks completely different. Then the last daytime estimate remains valid, and the system only checks for **sudden jumps** between consecutive frames.
- All measurements (time, dx, dy, rotation, scale, quality, status) are stored in the database. The table comes with the implementation. This also makes a slowly yielding mount visible.

## Consequences

- Stage 1 contains detection and warning; automatic compensation follows in stage 2. For stage 5 compensation is mandatory.
- Besides line and ROI, the calibration tool must also capture static zones.
- After large changes, for example a camera replacement, a recalibration remains necessary.

## Implementation note (2026-10-10)

Two false `Moved` alarms at dusk (shift around 900 px, "rotation" of 60–94°) were wrong feature matches: the estimated scale was near 0, which a fixed camera cannot produce. Two fixes:

- An estimate whose scale differs from 1 by more than `MaxScaleChange` (default 0.1) counts as `Unreliable`.
- A change must be confirmed by the very next check. Before, unreliable checks in between kept the candidate, so two bogus readings 45 minutes apart confirmed each other.
