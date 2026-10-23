# ADR-0013: Vehicle classes – COCO mapping, then an own model

- **Status:** accepted 2026-10-18; the point "trailers enclosed in one box with the towing vehicle" (Decision, Combinations) is superseded by [ADR-0039](0039-labelling-rules.md)
- **Date:** 2026-09-26
- **Stage:** 3

## Context

The target classes are car, van, truck, bus, motorcycle/scooter, bicycle/e-scooter and agricultural machines. COCO knows car, truck, bus, motorcycle and bicycle, but neither vans nor tractors. E-scooters mostly appear there only as person.

Agricultural machines are mostly tractors, often with one or two trailers. Such combinations are long, slow and need several seconds to cross the counting line. A COCO model usually detects the tractor as truck or car and the trailers often as separate truck boxes.

## Decision

- The target classes are a lookup table `VehicleClass` in the DB and an enum in code: 0 unknown, 1 car, 2 van, 3 truck, 4 bus, 5 motorcycle/scooter, 6 bicycle/e-scooter, 7 agricultural (tractor or agricultural machine, **including trailers as one vehicle**).
- **3a:** mapping car→car, truck→truck (van can be added heuristically via box size or proportions), bus→bus, motorcycle→motorcycle, bicycle→bicycle. Agricultural vehicles cannot be recognised with COCO and appear as truck or car in 3a.
- The class of a track results from a confidence-weighted majority vote over all frames. This is much more stable than the class of a single frame.
- **3b:** an own fine-tuning with exactly the target classes, based on images from the own camera. The labelling guideline (e.g. "when is it a van?") is recorded in `docs/labeling.md`.
- **Combinations:** a towing vehicle with trailers counts as **one** vehicle of the towing vehicle's class. This also applies to cars with caravans or trucks with trailers. In the own model (3b), trailers are not labelled as a class of their own but enclosed in one box with the towing vehicle. Until then – and as a safeguard afterwards – the counting logic recognises trailer tracks and does not count them (brought forward to stage 1): a track counts as a trailer if it follows another track in the same direction and lane, its box adjoins directly (gap up to about one vehicle height, slight overlap allowed), both travel at least about 8 km/h and the gap stays nearly constant for at least 0.6 s. Chains (tractor with two trailers) are assigned to the frontmost vehicle. The passage of the towing vehicle stores the number of trailers (`TrailerCount`). Because with the nearby camera a trailer only enters the image when the towing vehicle is already at the line, a passage is reported 3 s after the crossing at the earliest (ending tracks immediately).
- The model version is stored per `OperatingPeriod`, so that statistics remain interpretable across a model change.

## Open points

- How many examples per class are needed? Guideline: ≥ 300 instances per rare class (bus, motorcycle). With little bus traffic, collecting can take months; public datasets may be added (check the licence).
- Combination detection: check the thresholds (`TrailerSettings`) on reference videos with trailers and tractors. The residual risk lies with vehicles following closely at the same speed.
- Tractors are rare and seasonal (harvest). Public datasets from a traffic camera perspective contain hardly any tractors; own images are the main source here ([ADR-0018](0018-training-data.md)).
- **E-scooters:** the COCO model only detects the person on them, and `person` is deliberately not mapped; e-scooters are therefore not counted until 3b. Decision (2026-09-29): solve it first via the own training. If that is not enough, as a fallback count `person` tracks that cross the line faster than about 12 km/h (as bicycle/e-scooter; pedestrians walk at about 5 km/h).
