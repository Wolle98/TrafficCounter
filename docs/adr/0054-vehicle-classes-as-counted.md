# ADR-0054: Vehicle classes as counted with the own model

- **Status:** accepted
- **Date:** 2026-10-28
- **Stage:** 3
- **Supersedes:** the class list of [ADR-0013](0013-vehicle-classes.md) and, in [ADR-0039](0039-labelling-rules.md), the point that the counting adds e-scooters to bicycles

## Context

ADR-0013 fixed the target classes for the COCO model: 0 unknown to 7 agricultural, with bicycles and e-scooters as one class. Since then the classes changed step by step without a decision record of their own:

- E-scooters got a label of their own (ADR-0039). The plan was that the counting adds them to bicycles, so that statistics stay comparable. With the own model they are counted and shown as a class of their own, the app has its own colour for them, and the rules for slow e-scooters (ADR-0048, ADR-0050) need the class.
- Trailers got a class of their own; they are tracked but only counted with their towing vehicle (ADR-0047).
- Class 7 covers building-site machines as well as agricultural vehicles: excavators and dumpers at a building site are neither trucks nor cars. Its name changed from "agricultural" to "agricultural & construction" (version 0.10.15).

## Decision

The classes are, in the database (`VehicleClass`) and in code:

| Value | Class | Counted |
|---|---|---|
| 0 | unknown | yes |
| 1 | car | yes |
| 2 | van | yes |
| 3 | truck | yes |
| 4 | bus | yes |
| 5 | motorcycle/scooter | yes |
| 6 | bicycle | yes |
| 7 | agricultural & construction (tractors, agricultural machines, excavators, dumpers; with trailers as one vehicle) | yes |
| 8 | e-scooter | yes, as a class of its own |
| 9 | trailer | no, only with its towing vehicle (ADR-0047) |

Statistics show bicycles and e-scooters separately. Whoever needs the old combined class adds 6 and 8.

## Consequences

- Positive: the documentation matches what is stored; e-scooters can be evaluated on their own.
- Negative: bicycle figures before the own model contain no e-scooters either, since the COCO model detected none (ADR-0039, addendum of 2026-10-20); comparisons across the model change must keep that in mind.
