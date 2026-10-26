# ADR-0046: Two-wheelers are never trailers; one vehicle split into two tracks is counted once

- **Status:** accepted
- **Date:** 2026-10-25
- **Stage:** 1
- **Extends:** [ADR-0013](0013-vehicle-classes.md) (trailers), [ADR-0009](0009-counting-line.md) (counting line)

## Context

A check of the counted combinations in real operation showed two counting errors:

- **Cyclists in line:** cyclists riding directly behind each other meet every condition of the trailer coupling (same direction and lane, adjoining boxes, a minimum speed, a nearly constant gap for 0.6 s). The rear cyclist was taken for a trailer and not counted.
- **Split long vehicle:** a bus on the lane close to the camera fills most of the image width. The tracker sometimes splits it into a front and a rear track, both cross the line, and the bus was counted twice (0.2–0.3 s apart, same lane).

## Options considered

1. Tighten the coupling conditions for everybody (e.g. a longer coupling time): would also lose real trailers that enter the picture late.
2. **Exclude two-wheelers from the coupling, and drop a second motor-vehicle passage that crosses too soon after another in the same lane.**
3. Merge overlapping or adjoining boxes before tracking: would also merge cars following closely in queues.

## Decision

Option 2, in the counting engine:

- A bicycle, e-scooter or motorcycle (by the current class vote) is never a trailer and never tows one; a coupling is released as soon as either track's vote turns into a two-wheeler.
- A motor-vehicle passage within `CountingSettings.DuplicateSeconds` (0.4 s) of another one in the same direction and with a line position within `DuplicateLineTolerance` (0.08) is the same vehicle and is not counted again. Two real vehicles in one lane cannot be that close: at 30 km/h, 0.4 s are 3.3 m. Two-wheelers are exempt, since they ride side by side.

## Consequences

- Positive: groups of cyclists and long vehicles close to the camera are counted correctly.
- Negative: a child trailer behind a bicycle, if detected as a bicycle, is counted as a second bicycle; this is rarer than cyclists in line.
- Negative: a passage that really follows another one this closely in the same lane would be lost; for motor vehicles this is not possible, two-wheelers are exempt.
- Stored data from before the change is not corrected automatically.
