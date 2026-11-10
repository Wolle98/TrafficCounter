# ADR-0059: Car lane from the roof at night

- **Status:** superseded by [ADR-0060](0060-car-lane-from-the-bottom-at-night.md)
- **Date:** 2026-10-31
- **Stage:** 5
- **Extends:** [ADR-0056](0056-speed-from-camera-model-and-near-edges.md)

## Context

On the camera model the speed depends on the vehicle's lane: the counter puts the camera-near edges into the lane, and the scale along the road changes by about 7–8 % per meter of lane on the main camera. The lane comes from the box bottom near the principal point's column (ADR-0056).

At night, headlight light on the road stretches the box downwards, so the box bottom shows the vehicle nearer to the camera than it is. The lane from the box top is an independent estimate: there the top is the roof's far edge, at roof height and a class width behind the near side.

Measured on 1,408 passages of 33 recordings of the main camera, cars, roof height 1.37 m (the height at which both estimates agree in daylight):

| Recordings | Lane from bottom minus lane from top (median) |
|---|---|
| Day (28 recordings) | −0.15 … +0.16 m |
| Dusk, sun above −6° (one hour) | −0.15 m, also in its darkest five minutes |
| Night (4 recordings) | +0.14 … +0.73 m, per passage up to more than 1 m |

At night the lane from the top stays at the daylight lanes of each direction; the lane from the bottom moves 0.5–1 m towards the camera. That makes night speeds of cars about 4–5 % too low, single ones more than 8 %.

Per passage the lane from the top is less precise in daylight than the lane from the bottom: the roof height differs between cars (half of them within ±0.4 m of lane). For vans, trucks and buses the heights differ too much to use the top at all; two-wheelers showed no clear stretch.

## Options considered

1. Keep the lane from the bottom: night speeds of cars stay too low.
2. Subtract a constant offset at night: the stretch differs between nights (0.14 to 0.73 m).
3. Clamp the lane from the bottom to the lane from the top plus a margin, day and night: changes up to a third of the cars in daylight, where the bottom is right.
4. **At night, take the lane of cars from the box top with a roof height of 1.37 m.**

## Decision

We choose option 4, because it follows the actual stretch per passage and leaves daylight unchanged.

- Night is the sun below −6° at the site (`LightingClassifier`, ADR-0017); in dusk the stretch was not measurable.
- Only cars; other classes keep the lane from the bottom.
- The speed fit itself is unchanged.

## Consequences

- Positive: Night speeds of cars no longer depend on how far their light reaches; no constant to maintain.
- Negative / risks: Per passage the night lane is as uncertain as a car's roof height (an SUV comes out up to a meter too far, its speed a few percent too high). There is no independent speed reference at night.
- Follow-up work: Check with a test drive at night. Look at vans and trucks at night once there are enough of them.
