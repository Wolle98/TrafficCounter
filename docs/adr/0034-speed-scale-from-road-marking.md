# ADR-0034: Speed scale from a measured road marking

- **Status:** accepted
- **Date:** 2026-10-06
- **Stage:** 5
- **Extends:** [ADR-0033](0033-speed-scale-from-vehicle-lengths.md) (scale from vehicle lengths), [ADR-0015](0015-speed-measurement.md)

## Context

The scale from vehicle lengths (ADR-0033) depends on an assumed mean car length. It was also interpolated linearly between the kerbs. At the site there is a painted marking with known dimensions: length in the direction of travel, width across it and distances to both kerbs (values in the private repo).

For points on the road plane, the scale (metres per pixel) is proportional to the distance from the camera. The reciprocal of this distance changes linearly with the image row. Linear interpolation in metres per pixel is therefore off by a few percent in the middle of the road.

## Decision

- **Pinhole camera model:** the image rows of the kerbs and the marking edges along the counting line, together with the length of the marking in pixels, determine focal length, height, tilt and distance of the camera. From this follows the scale at both kerbs (`FarKerbMetersPerImageWidth`, `NearKerbMetersPerImageWidth`). This anticipates the homography from ADR-0015, but only along the counting line.
- **Interpolation:** between the kerbs, **pixels per metre** are interpolated linearly, not metres per pixel. This is exact for the road plane.
- **Role of the vehicle lengths:** the scale from vehicle lengths (ADR-0033) remains the check or the fallback where there is no marking.

Result at the site: the scale from vehicle lengths was on average 4 % too high and overestimated the perspective. At the far kerb it was 3 % too large, at the near kerb 5 % too small. The camera model matches the four image rows to within 2 pixels.

## Consequences

- Positive: the scale is based on measurements at the site instead of an assumption about vehicles. The remaining uncertainty comes from measuring the marking (a few centimetres) and from the bottom edge of the box not being exactly the ground point.
- Follow-up work: a test drive with known speed as an independent check ([ADR-0035](0035-test-drives-for-speed-validation.md)). The camera model belongs into the calibrator as a calibration step (click points, enter distances).
