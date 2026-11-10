# ADR-0060: Car lane from the box bottom at night

- **Status:** accepted
- **Date:** 2026-11-02
- **Stage:** 5
- **Supersedes:** [ADR-0059](0059-car-lane-from-the-roof-at-night.md)

## Context

ADR-0059 took the lane of cars at night from the box top with a roof height of 1.37 m, because the box bottom was assumed to move 0.5–1 m towards the camera in headlight light. It had no independent speed reference at night and asked for a test drive.

Two references now exist, both from one recording of the main camera in full darkness:

**Wheel hubs, all cars.** A hub is tracked on the plane at hub height, which needs neither a lane nor a roof height (ADR-0056). Median against the hub, 150 cars and the 60 cleanest of them:

| Lane | All (150) | Cleanest (60) | Spread (all / cleanest) |
|---|---|---|---|
| Roof, 1.37 m (ADR-0059) | +8.3 % | +5.5 % | 6.8 / 6.1 % |
| Roof, 1.50 m | +3.6 % | +1.1 % | not computed |
| Roof, 1.55 m | +1.8 % | −0.8 % | not computed |
| **Box bottom** | **+2.8 %** | **+1.1 %** | **5.4 / 3.9 %** |

Both directions agree. The lane that fits the hub lies 0.1–0.3 m from the lane from the bottom and 0.6–0.9 m from the lane from the roof, so the stretch of the box is much smaller here than ADR-0059 assumed.

**Speedometer, one test vehicle.** Five steady drives of a vehicle well above 1.37 m: roof lane at 1.37 m +9.8 %, at the vehicle's real height −1.0 %, box bottom −2.3 % (median −4.1 %). The roof lane works when the height is right, but the counter does not know the height of a car; this vehicle's hubs read 3 % under its speedometer on average (four drives), so the hub figures above may be up to that much too high. The distance between the lanes stays about 5 points either way.

## Options considered

1. Keep the roof lane at 1.37 m: too high for ordinary cars and more so for tall ones.
2. Raise the constant to 1.50–1.55 m: the median fits as well as the box bottom, but tall and low cars stay wrong by about 3 % per 0.1 m of height.
3. Halfway between the two lanes: not clearly better than the box bottom (+3.7 % against the speedometer), and it still depends on a height.
4. **Take the lane from the box bottom, day and night, as before ADR-0059.**

## Decision

We choose option 4, because it is nearest to both references, spreads least and needs no assumption about a car's height.

## Consequences

- Positive: One rule for all light conditions; night speeds no longer depend on a car's height. The sun position is no longer needed for the speed.
- Negative / risks: The lane still moves with the light on the road; a strong stretch in other recordings would lower single night speeds by some percent. The evidence is one recording and one test vehicle, so this gives no correction factor.
- Follow-up work: Speeds of cars stored at night since 0.14.0 are too high by about 5–7 % against the lane from the bottom; whether they are scaled (ADR-0057) is decided separately. Check the wheel hubs on further night recordings. The second camera is not affected by this change: on its camera model the edge search finds no position, so its speed comes from the ground points.
