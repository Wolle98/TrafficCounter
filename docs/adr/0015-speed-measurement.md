# ADR-0015: Speed measurement via a homography of the road plane

- **Status:** accepted 2026-10-18; so far a first step without homography is implemented with [ADR-0033](0033-speed-scale-from-vehicle-lengths.md) and [ADR-0034](0034-speed-scale-from-road-marking.md); extended by [ADR-0052](0052-standing-vehicles-and-stops.md) (no speed through a stop)
- **Date:** 2026-09-26
- **Stage:** 5

## Context

The camera is mounted on the first floor (height about 4–5 m) and 7–8 m away from the street, i.e. looking down at an angle. Pixel distances correspond to different metres depending on the image position. At 50 km/h (≈ 14 m/s) and a visible road section of about 20 m, a vehicle is in the image for only ~1.4 s; at 25 fps that is ~35 measuring points.

## Decision

- Calibration: at least 4 points on the road with measured distances (lane markings, kerb joints, placed markers) → `Cv2.FindHomography` maps image coordinates to the road plane in metres.
- The measuring point is the ground contact (centre of the bottom edge of the box). For tall vehicles this causes a systematic error, which is corrected against the test drives.
- Speed: linear regression of the road coordinate over the frame timestamps within the measuring area, not a two-point difference. The standard deviation is stored as a quality measure.
- Prerequisites: ≥ 20 fps without frame loss in the measuring area and a short exposure time (motion blur); adjust the camera settings if necessary.

## Consequences

- The accuracy is determined with test drives; about ±5–10 % is expected.
- The values are statistical estimates and explicitly not a basis for traffic offence proceedings.
