# ADR-0048: E-scooters at walking pace on the sidewalk are not counted

- **Status:** accepted
- **Date:** 2026-10-26
- **Stage:** 3
- **Extends:** [ADR-0020](0020-local-traffic-situation.md) (sidewalk motorcycle rule, implementation note of 2026-10-15)

## Context

The own model now and then takes a pedestrian on a sidewalk for an e-scooter. On 2026-10-26 the second camera counted three such "e-scooters", all on the far sidewalk at 3.9–4.7 km/h. The real e-scooters of the same day rode at 14–21 km/h, also those on a sidewalk. For prams taken for motorcycles there is already a rule: a motorcycle outside the kerbs slower than `Site:SidewalkMotorcycleMaxKmh` (10 km/h) is not counted.

## Options considered

1. Wait for the next model: pedestrians as training images without boxes help, but take time and rarely remove all mistakes.
2. **Apply the sidewalk rule to e-scooters as well, with a limit of their own.**

## Decision

Option 2. An e-scooter passage outside the kerbs slower than `Site:SidewalkEScooterMaxKmh` (8 km/h) is not counted. Without kerbs or a measured speed the rule does not apply. The model is trained further all the same.

## Consequences

- Positive: pedestrians taken for e-scooters are no longer counted.
- Negative: an e-scooter pushed along the sidewalk is not counted either; it is not ridden, so that is acceptable.
