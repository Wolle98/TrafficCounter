# ADR-0050: E-scooters at walking pace are not counted on the road either

- **Status:** accepted
- **Date:** 2026-10-27
- **Stage:** 3
- **Extends:** [ADR-0048](0048-slow-e-scooters-on-the-sidewalk.md) (e-scooters at walking pace on the sidewalk are not counted)

## Context

ADR-0048 drops e-scooters slower than 8 km/h only outside the kerbs. On 2026-10-27 a pedestrian who crossed the road diagonally at the counting line was counted as an e-scooter at 5.4 km/h: at the line she was on the road, so the rule did not apply.

The e-scooter speeds measured since the speed scale exist fall into two groups with nothing between 8 and 12 km/h: 12 passages at 1.9–7.5 km/h and all others at 12 km/h or more, most at 16–22 km/h. Real e-scooters do not ride at walking pace here, neither on the road nor on the sidewalk.

## Options considered

1. Keep the rule on the sidewalk only and wait for the model to learn pedestrians.
2. **Drop every e-scooter passage slower than the limit, wherever it crosses the line.**

## Decision

Option 2. An e-scooter passage slower than `Site:SlowEScooterMaxKmh` (8 km/h, formerly `Site:SidewalkEScooterMaxKmh`) is not counted; the kerbs are no longer needed. Without a measured speed the rule does not apply. The motorcycle rule keeps its sidewalk condition, since a slow motorcycle on the road (turning, in a queue) is real.

The 12 slow e-scooter passages already stored were deleted from the database.

## Consequences

- Positive: pedestrians taken for an e-scooter are no longer counted, also when they cross the road.
- Negative: an e-scooter that really rides slower than 8 km/h, for example when it starts, is not counted; none was measured so far.
