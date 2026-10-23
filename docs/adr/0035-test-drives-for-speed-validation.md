# ADR-0035: Test drives for speed validation via recording and marking afterwards

- **Status:** accepted
- **Date:** 2026-10-07
- **Stage:** 5

## Context

The speed scale comes from vehicle lengths ([ADR-0033](0033-speed-scale-from-vehicle-lengths.md)) and a measured road marking ([ADR-0034](0034-speed-scale-from-road-marking.md)). Both are indirect. The measurement is only validated by drives at a known, constant speed (cruise control or GPS speedometer).

During the test drives, normal traffic is on the road. Noted times are not enough to reliably distinguish the test vehicle from other vehicles passing in the same seconds.

## Options considered

1. **Note the times** and take the counted passage closest in time: simple, but ambiguous with other traffic.
2. **A test mode of its own in the service** that shows speeds live: additional code in the 24/7 service, the same assignment question.
3. **Recording by the service** ([ADR-0032](0032-recordings-by-the-service.md)) and marking the test drives afterwards in the annotator with the driven speed; the evaluation compares with the service's stored measurement.

## Decision

We choose option 3. The test vehicle is clearly recognisable in the video, and nothing changes in the service.

- **Recording:** a slot in `schedule.json` with the tag `speedtest`. The service keeps counting normally and stores its speeds.
- **Marking:** `Annotator <video> --speed true`. Only the test drives are marked. After each mark, the annotator asks for the driven speed and proposes the previous one. The marks go into a separate file `<video>.speed.csv` in the ground truth format with an additional column `speed_kmh`. They are not a complete count and therefore do not enter the counting accuracy. In a normal ground truth, a passage can also get a known speed with `v`.
- **Evaluation:** `Regression --speed true --live true` assigns each test drive the nearest counted passage of the same direction within the tolerance. It reports the deviation per drive and the mean error, overall and per direction. Without `--live` the detection runs again over the video, for example to check a changed scale.

## Consequences

- Positive: unambiguous assignment even with traffic; the measurement is checked in real operation (frame rate, load); the recordings can be recomputed after later scale changes.
- Negative / risks: speedometer and cruise control have a tolerance of a few percent themselves. A GPS display or several drives per speed average this out.
- Follow-up work: test drives at two to three speeds (e.g. 20, 30, 40 km/h) in both directions; correct the scale per lane if the directions deviate systematically.

## Addendum 2026-10-07: speedometer reading instead of GPS value

What is entered is the speedometer reading when crossing the line, in whole km/h. Even with cruise control it fluctuates by 1–2 km/h, so the reading at the line counts, not the set value. The regression converts it into the true speed with a speedometer calibration of the test vehicle: true = factor · reading + offset, determined from speedometer against GPS. The calibration belongs to the vehicle and is therefore in the private configuration (`TrafficCounter:SpeedTest:SpeedometerFactor`, `SpeedometerOffsetKmh`).
