# ADR-0047: The own model's trailers are tracked and only counted with their towing vehicle

- **Status:** accepted
- **Date:** 2026-10-26
- **Stage:** 3
- **Extends:** [ADR-0013](0013-vehicle-classes.md), [ADR-0039](0039-labelling-rules.md), [ADR-0046](0046-two-wheelers-and-split-tracks.md)

## Context

The labelling rules (ADR-0039) give a trailer a box of its own with the class `Trailer`. When the own model went live, the detector left that class unmapped, so a trailer could never be counted as a vehicle. As a side effect its detections were dropped before tracking: the trailer coupling of the counting engine never saw them, and a car with a trailer was stored with `TrailerCount` 0. With the COCO model a trailer was usually detected as `truck`, tracked and coupled.

## Options considered

1. Keep the class unmapped and map it to `truck` instead: the trailer is coupled, but an uncoupled trailer would be counted as a truck.
2. **A vehicle class `Trailer` that only exists inside the counting:** tracked, coupled like any other track, never emitted as a passage.

## Decision

Option 2. The detector maps the model class `trailer` to `VehicleClass.Trailer`. The counting engine never emits a track whose class vote is `Trailer`; it can only become the trailer of the vehicle in front and raises that passage's `TrailerCount`. Passages and the database never contain the class.

## Consequences

- Positive: combinations are counted with their trailers again, as with the COCO model.
- Negative: a trailer whose towing vehicle was not tracked (occlusion) counts nowhere, as before.
- Stored passages counted with the own model before this change (from 2026-10-23 09:10) may lack their trailers; they cannot be corrected without images.
