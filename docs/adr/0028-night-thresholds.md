# ADR-0028: Separate confidence thresholds for the night

- **Status:** accepted
- **Date:** 2026-09-30
- **Stage:** 1

## Context

At night the COCO model sees vehicles mainly by their headlights and tail lights. In front of the counting line its confidence often stays at 0.1–0.4 and only rises above the tracker's thresholds behind the line: high detection from 0.5, new track from 0.6. The track is then created too late and the vehicle is not counted ([ADR-0017](0017-camera-and-night-operation.md)).

Measurement with hand-counted reference recordings:

| Thresholds (high / new track) | Night 21:00 + 23:00 (32 veh.) | Day, school time (259 veh.) |
|---|---|---|
| 0.5 / 0.6 | 19 correct, 0 extra, **59 %** | 243 correct, 1 extra, 93.1 % |
| 0.3 / 0.3 | 27 correct, 1 extra, 82 % | – |
| 0.2 / 0.25 | 30 correct, 3 extra, **86 %** | 247 correct, 4 extra, 93.9 % |

At night the low thresholds help a lot. During the day they are about neutral: they find a few additional two-wheelers but also produce false counts, including a bus tracked twice.

## Decision

- The tracker gets the lighting conditions from the sun position (`LightingClassifier`) for every frame and uses separate thresholds **at night**: `NightHighConfidence` 0.2 and `NightNewTrackConfidence` 0.25. During the day and at dusk, 0.5 / 0.6 still apply.
- Dusk keeps the day values for now, until the dusk video has been counted.
- The thresholds are configurable (`TrafficCounter:Tracker`).

## Consequences

- Positive: night counting rises from about 60 % to about 85 % accuracy without changing day counting. This is an interim solution until the own model with night images (stage 3).
- Negative / risks: the data basis of 32 night vehicles is small. At night there are somewhat more false counts, for example from light reflections. Check again after further night recordings and with rain.
- Follow-up work: evaluate dusk; determine the thresholds anew after training.
