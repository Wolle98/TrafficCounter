# ADR-0061: Car factor for night speeds stored with the roof lane

- **Status:** accepted
- **Date:** 2026-11-02
- **Stage:** 5
- **Extends:** [ADR-0057](0057-class-factors-for-stored-speeds.md), [ADR-0060](0060-car-lane-from-the-bottom-at-night.md)

## Context

Version 0.14.0 took the lane of cars at night from the roof (ADR-0059), version 0.15.0 from the box bottom again (ADR-0060). The car speeds the main camera stored at night in between are too high against the lane from the bottom. The counts are not affected, nor are day and dusk (sun above −6°), other classes or the second camera.

How much too high, by reference:

| Reference | Effect of the change to the bottom lane |
|---|---|
| Replay of one night recording with both versions, 38 pairs of the same cars | −7.3 % |
| Wheel hubs of the same recording, all cars (150) / cleanest (60) | −5.1 % / −4.2 % |
| Speedometer, one test vehicle well above the assumed roof height | −11 % |
| Second camera, paired cars: 0.14.0 at night against 0.15.0 at night (67 pairs after the change) | about −5 % |

## Options considered

1. **Leave the stored speeds as they are:** cars at night stay some percent too fast for these two and a half days, and no other period compares with them.
2. **Recompute every passage:** there are recordings only for a few hours.
3. **Multiply the stored car speeds of these nights by one factor,** with the procedure of ADR-0057.

## Decision

We choose option 3 with **×0.93**, the replay's median rounded, because the replay compares the same vehicles on the same frames and so measures only the change of lane.

- Range: passages of the counter of the main camera's counting machine, class car, while version 0.14.0 ran and the sun was below −6° at the site, the condition under which that version used the roof lane. That makes three windows.
- Tool: `TrafficCounter.Sync scale-speeds`, once per window, in both databases. The backup files stay in the private repository.

The windows were checked against the second camera: its distance to the main camera jumps at the sun's −6° and not at sunset, so dusk stays out.

## Consequences

- Positive: Night speeds of cars are comparable before and after 0.14.0, within about 2 %.
- Negative / risks: A factor corrects the mean, not the single passage. The gap between the lanes grows with speed in the fast passages looked at (9–21 %), so single high values stay somewhat too high. Against the second camera the scaled nights lie about 2 % below the first night of 0.15.0, on few pairs; apart from the single test vehicle, the references leave ×0.93 to ×0.96 open.
- Follow-up work: None. Restoring or scaling again is possible from the backup files.
