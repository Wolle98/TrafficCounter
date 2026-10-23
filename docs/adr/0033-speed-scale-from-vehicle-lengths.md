# ADR-0033: Speed first with a scale from vehicle lengths

- **Status:** accepted, scale replaced by [ADR-0034](0034-speed-scale-from-road-marking.md) (road marking)
- **Date:** 2026-10-06
- **Stage:** 5

## Context

[ADR-0015](0015-speed-measurement.md) plans a homography of the road plane, calibrated with at least four measured points on the road. That requires someone to measure on the street. Until then there should already be speeds, at least as a statistical estimate.

The camera looks almost perpendicularly at the street, and the vehicles drive across the image. At the counting line, the box width of a car therefore roughly equals its length. Cars are about 4.5 m long on average; individual ones range from about 3.6 to 5.2 m.

## Options considered

1. Wait with speeds until points have been measured.
2. Scale from vehicle lengths: the median of the box widths of many cars per lane, converted with 4.5 m.
3. Scale from a known distance in the image, for example a lane marking. There is none here.

## Decision

We choose option 2 as a first step.

- `Regression --calibrate-speed true` measures cars near the counting line in the reference videos: completely in the image, not occluded, one value per vehicle (median).
- The box width is fitted as a straight line over the position along the counting line. The lane determines the distance to the camera and thus the size in the image. Outliers such as vans and occluded vehicles are left out in the second pass.
- The width at the kerbs gives a scale: metres per image width at the far and at the near kerb (`FarKerbMetersPerImageWidth`, `NearKerbMetersPerImageWidth` in the site configuration). In between it is interpolated linearly.
- Measurement as in ADR-0015: the horizontal position of the ground point (centre of the bottom edge of the box) in a zone around the counting line, a fitted line over the frame timestamps. This gives the speed, converted with the scale of the lane the vehicle drives on.

The homography from ADR-0015 remains the goal. As soon as points have been measured or a test drive with known speed is available, it replaces or corrects this scale.

## Consequences

- Positive: speeds immediately, without work on the street, for all previous reference videos and in operation.
- Negative: the scale is as accurate as the assumed mean car length; about ±10 % is expected. All speeds share this error. Distributions and comparisons (day/night, direction) are hardly affected, absolute values are.
- Follow-up work: test drive with known speed (GPS) for correction; later measured points for the homography.
