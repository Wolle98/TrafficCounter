# ADR-0062: Speed zone grows to twelve positions

- **Status:** accepted
- **Date:** 2026-11-02
- **Stage:** 5
- **Extends:** [ADR-0056](0056-speed-from-camera-model-and-near-edges.md)

## Context

The zone of the speed fit is ±0.2 of the image width and grows up to ±600 px at 1920 only until it has the minimum of eight positions (ADR-0056). A fast vehicle therefore often gets a speed from exactly those eight, and the highest stored speeds are the least reliable ones.

Vehicles drive slightly diagonally on this road, while the fit puts all edges on one lane. Left and right of the image centre the edges therefore give different speeds, up to 44 % apart. Over a whole zone this evens out; with few positions the speed depends on where they lie. One scooter stored at 82 km/h gave 82 from the positions on one side and 65 from those on the other. Independent measures of it gave 63–69 km/h.

Speed clips of the main camera were replayed. The reference is a patch around the wheels tracked in the image, without detector boxes; its absolute accuracy is only about ±5 %, and the clips are selected by high speed, so they contain many randomly high values.

Widening the zone for all vehicles is not an option: against wheel hubs at night the spread rose from 4.4 % to 13.0 %, because it takes in braking and pulling away.

## Options considered

1. **Keep the zone:** high single values stay up to about 30 % too high.
2. **±800 px for all vehicles:** see above.
3. **Weigh both sides of the image centre equally:** at higher speeds it reads 8 % low against the hubs (6 vehicles).
4. **Let the zone grow in its 50 px steps until it has twelve positions, up to ±800 px at 1920.** Eight positions remain the minimum for a speed.

## Decision

We choose option 4, because it only affects vehicles with few positions, which are the fast ones and those with gaps in their track.

- `SpeedWantedSamples` = 12, `SpeedMaxZoneFraction` = 0.41667 (800 px at 1920). `SpeedMinSamples` stays 8.

Clips, median / half spread (p84−p16) / largest deviation against the reference:

| Class | Clips | 0.15.0 | This decision |
|---|---|---|---|
| Car | 21 | −2.2 / 8.1 / +31.4 % | −2.2 / 7.5 / +14.8 % |
| Two-wheelers | 12 | +1.3 / 6.0 / +15.1 % | −0.4 / 4.7 / +15.2 % |
| Vans, trucks | 8 | −1.5 / 4.4 / +7.3 % | −0.8 / 4.6 / +7.7 % |

Two recordings of one hour each, one at night and one by day, replayed with both versions: the same passages and classes, no speed lost or gained, about 4 % of the speeds changed, the median of all 0.0 %. Of the speeds from 35 km/h, 40 % (night) and 22 % (day) change. The changed speeds move by −5 to +4 %.

## Consequences

- Positive: The largest errors of fast vehicles get smaller. Counts and classes did not change in the replays, and ordinary speeds stay as they were.
- Negative / risks: The benefit is shown on the clips; only few fast vehicles have a wheel hub as a second reference (15 in the comparison, no harm seen). Single clips get worse (one car from +8 % to +15 %), and four of 41 stay at +10 % or more, because light on the road and gaps in the detections remain. A high single value stays a hint, not a measurement. Stored speeds are not recalculated.
- Follow-up work: None planned. The second camera gets the same rule with its next update; its effect there was not measured.
