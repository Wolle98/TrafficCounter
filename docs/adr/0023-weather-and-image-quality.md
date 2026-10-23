# ADR-0023: Weather, backlight and image quality – measures, monitoring, weather data

- **Status:** accepted, extended by [ADR-0024](0024-operations-log-and-logging.md) (minute statistics)
- **Date:** 2026-09-26
- **Stage:** 1 (monitoring), 3 (training data), 4 (weather data)
- **Extends:** [ADR-0017](0017-camera-and-night-operation.md), [ADR-0018](0018-training-data.md), [ADR-0020](0020-local-traffic-situation.md)

## Context

The camera is behind a window pane in a facade on the **weather side**. Depending on the wind, rain hits the pane directly. This leads to the following disturbances:

| Disturbance | Effect in the image | Risk for counting |
|---|---|---|
| Drops and rivulets on the pane | Very blurred spots and local loss of contrast, because the drops are only centimetres in front of the lens. At night halos around street lamps and headlights. | Vehicles are missed in affected areas. |
| Fogged pane | Inside in winter, outside through dew or frost: grey haze up to complete failure. | Vehicles are missed, up to complete failure. |
| Wet snow on the pane | Partial or complete blindness. | Failure. |
| Wet road | Reflections of vehicles and headlights below the vehicles. | Rare but possible: a "ghost vehicle" that is counted twice. |
| Falling rain or snow, spray | Less contrast; at night drops and flakes light up in the lamp light. | Uncertain detection, occasionally missed vehicles. |
| Snow cover | The road becomes bright, white vehicles stand out poorly. | Uncertain detection. The counting logic is not affected because it does not depend on the colour of the road. |
| **Backlight** | Sun in the afternoon and evening in front of the camera, especially in winter: glare, lens flares; dirt and drops on the pane light up. | Loss of contrast, vehicles appear as silhouettes. |

YOLO detects objects, not bright spots. Drops, flakes and light reflections therefore hardly cause false alarms. ByteTrack keeps following vehicles even across uncertain detections. The main damage is therefore **missed** vehicles. Without monitoring, these losses go unnoticed.

## Decision

1. **On-site measures** (mounting checklist, also applies after a camera change):
   - **Water-repellent coating** on the outside of the pane in the camera area, for example a glass sealant like for car windscreens; renew regularly. Because the pane is on the weather side, this measure has priority.
   - **Anti-fog agent** on the inside. The black hood against reflections must not prevent air circulation at the pane.
   - **Lens as close to the pane as possible**, preferably below a roof overhang or roller shutter box.
   - **Lens hood or sun shade** above the lens; tilt the camera slightly downwards so that the sky is not in the image. The houses opposite partly hide the lowest sun.
   - **Clean the pane regularly**, because dried drops leave permanent spots.
2. **Image quality monitoring** (stage 1):
   - The service stores measurements **per minute** in a table of its own: brightness, contrast, sharpness, measured fps, number of detections.
   - In addition it stores the sharpness **per image region**, as a grid over the evaluation band, focusing on the area around the counting line. This shows whether a drop or spot sits at the decisive place.
   - These are pure measurements, no images (ADR-0005), at about 1,440 rows per day.
3. **Warning levels:**
   - The basis is a moving normal value **per time of day and lighting condition**, so that night does not count as a disturbance.
   - From it follows the quality level: `good`, `limited` or `unusable`.
   - Level changes are logged; at `unusable` there is a warning in the Windows event log.
   - The level is linked to the passages and minute values. Analyses (stage 4) mark such periods like downtimes.
   - The thresholds are set based on the reference videos and real operation.
4. **Reflections:**
   - "Ghost vehicles" from reflections on a wet road are a separate test scenario.
   - A filter is only added if the problem actually occurs in the rain videos. It would discard a detection or track that lies directly below another one, moves in sync and has the same width.
5. **Reference videos and training data** (extends [ADR-0018](0018-training-data.md)):
   - **Further categories:**
     - rain by day and at night;
     - snowfall;
     - snow cover;
     - drops on the pane;
     - fogged pane;
     - wet road with reflections;
     - backlight.
   - Own images of these situations go specifically into the fine-tuning.
   - Public datasets for bad weather (e.g. DAWN, AAU RainSnow) can be added after a licence check.
6. **Targets per weather situation** (extend the stage 1 acceptance):
   - Rain by day ≥ 90 %, rain at night ≥ 80 %.
   - Snow, drops on the pane and backlight: best effort, **but reliably marked as `limited` or `unusable`**. Honest marking comes before embellished counts.
7. **Weather data** (stage 4):
   - Hourly import of precipitation, snow depth, temperature, wind and sunshine from the open data of the German Weather Service (DWD), for example via the Bright Sky API (free, no API key), into a table of its own.
   - **Privacy:** the configuration contains only the **ID of the nearest DWD station**, no coordinates. The ID is in the private site configuration; the public code contains no reference to the location.
   - Purpose: analyses such as traffic in rain versus dry weather or cycling by temperature, and checking whether quality drops coincide with precipitation or sunshine.

## Consequences

- Two new tables: image quality per minute (stage 1) and weather per hour (stage 4).
- Drops in the counts can be classified later: less traffic, a bad image or a failure.
- The mounting checklist requires some upkeep (renew the coating, clean the pane). This also applies to a later outdoor camera, then to its front glass.
- An outdoor camera below a roof overhang would be more robust against drops. That is a further argument for the switch path from ADR-0017 if the monitoring shows frequent limitations.

## Implementation note (2026-10-07)

The weather import (point 7) runs in the service every hour via Bright Sky into `WeatherHour`; `TrafficCounter.Sync weather` imports past periods. The reflection filter (point 4) does not exist yet; the first rain recordings decide whether it is needed.

## Implementation note (2026-10-10)

On dark or rainy nights the level flipped between `Unusable` and `Good` within seconds, 64 `QualityChanged` events in one night. The event (and its log line) is now written only after a new level has held for `QualityEventHoldSeconds` (default 180 s, `SettledQualityLevel`). A change that returns to the reported level within that time is not reported. The per-minute statistics keep the immediate level.
