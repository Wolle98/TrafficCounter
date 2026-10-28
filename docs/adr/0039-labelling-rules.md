# ADR-0039: Labelling rules for own training images

- **Status:** accepted 2026-10-18, extended by [ADR-0047](0047-trailer-class-tracked.md) (trailers tracked); the point that the counting adds e-scooters to bicycles is superseded by [ADR-0054](0054-vehicle-classes-as-counted.md); small vans: [ADR-0055](0055-small-vans.md)
- **Date:** 2026-10-11
- **Stage:** 3
- **Supersedes:** in [ADR-0013](0013-vehicle-classes.md) the point "trailers are not labelled as a class of their own but enclosed in one box with the towing vehicle"

## Context

The collected samples (ADR-0026) are labelled semi-automatically in Label Studio (ADR-0018). Before the first image is labelled, the rules must be fixed, otherwise the training set becomes inconsistent. Two cases are open, and the counting result depends on them most:

- **Towing vehicle with trailer** (tractor, truck, car with caravan). ADR-0013 planned one box around the whole combination.
- **Bicycle and e-scooter with rider.** COCO, on which the current model is trained, labels the bicycle and the person separately; the bicycle box does not contain the rider.

The camera is close to the road. A combination is often longer than the image: when the trailer enters, the towing vehicle has already left on the other side.

## Options considered

Combinations:

1. One box around towing vehicle and trailers (ADR-0013).
2. Separate boxes: towing vehicle in its class, each trailer as class `Trailer`.

Bicycles:

1. Box around the bicycle only (as in COCO).
2. Box around bicycle and rider together, class `Bicycle` (as "cyclist" in traffic datasets such as KITTI).

## Decision

**Combinations: option 2, `Trailer` as a class of its own.**

- One box around a combination has no consistent shape here. It is cut off at the image edge, its length depends on the number of trailers, and while the combination drives through the image the visible part changes from "tractor" to "tractor plus trailer" to "trailer only". A model learns such boxes badly, and the box centre jumps, which disturbs the tracker.
- A trailer looks like a trailer, whatever pulls it. As a class of its own it is easy to learn and needs few examples.
- Counting becomes simpler and more robust: tracks of class `Trailer` are never counted. They only raise the trailer count of the vehicle in front. The distance heuristic of ADR-0013 then only remains as a fallback.

**Bicycles: option 2, one box around bicycle and rider, class `Bicycle`.** The same applies to e-scooters and motorcycles with rider. **E-scooters get a label of their own, `EScooter`:** a standing rider on small wheels without a frame looks quite different from a cyclist, and two clear classes are easier to learn than a mixed one. The counting adds both up as "bicycle/e-scooter" (ADR-0013), so statistics and app stay as they are, and e-scooters could later be reported separately without labelling again.

- The rider is the largest and most visible part. The thin frame of a bicycle almost disappears at night, in motion blur and at 640×192 input. With the rider the object is bigger and more distinctive, so recognition chances rise most here.
- No separate person box on a bicycle. A person box on the road is then always a pedestrian, which is ignored for counting anyway.
- Fine-tuning has to unlearn the COCO meaning of "bicycle". That needs some hundred examples, which is why bicycles are collected preferentially (ADR-0026 addendum).

**Further rules:**

- **Edge of the image:** label a vehicle cut off at the edge if at least about a third of it is visible. Draw the box only up to the edge.
- **Occlusion:** label occluded vehicles if they are clearly recognisable. Draw the box around the visible part.
- **Parked vehicles** in the band: label them too (the model must see them; the counting ignores them because they do not cross the line).
- **Pixelated areas** (number plates, faces, ignore zones): label as usual; ignore zones need no boxes.
- **Unsure class:** choose the most likely class rather than leaving a vehicle out. Use van for small delivery trucks up to about 3.5 t.
- **Road markings at night:** the box encloses the vehicle body only, not the painted road marking that the headlights light up (nor reflections on a wet road). The current model often includes the marking: its car boxes at night are about two thirds wider than by day (median 688 px instead of 414 px at the same height). Delete proposals that enclose only the marking.
- **Pedestrians:** no boxes (the counting does not need them, and the anonymisation uses its own model).
- **No vehicle in the image:** submit without boxes; it is a valid negative example.

## Consequences

- Positive: consistent labels; trailers and bicycles, the weakest classes so far, get the forms that are easiest to learn.
- Negative: an additional class `Trailer` for the detector, and the counting logic must skip it (stage 3 follow-up). The COCO model before fine-tuning knows no trailers, so the proposals for trailers have to be corrected by hand.
- Hand counts record e-scooters as class `EScooter` (value 8) since 2026-10-16; the annotator can reclassify older bicycle entries (`b`, then `8`). The counter still reports them as bicycles, so the regression shows them as "EScooter -> Bicycle" in the class comparison.
- Follow-up: once accepted, mark ADR-0013 as partly superseded; the detector maps the model class `Trailer` to "not counted, trailer of the vehicle in front".

## Addendum 2026-10-20: e-scooters are not detected at all

The consequence above assumed that the counter reports e-scooters as bicycles. The hand counts show otherwise: of 44 e-scooters in seven hand counts (29.09. to 19.10., day, dusk and night, both cameras), the current model counted none, neither as bicycle nor as any other class. The bicycle figures therefore contain no e-scooters, and the app labels the class "bicycle" only. Until the own model is trained, e-scooters are simply missing from the counts.

## Addendum 2026-10-20: quads

Quads (all-terrain vehicles) are labelled as `Motorcycle`: the rider sits on top as on a motorcycle, size and shape are closer to a motorcycle than to a car, and road-registered quads belong to the same vehicle category L. This also holds for quads used as work vehicles; `Agricultural` stays reserved for tractors and agricultural machines.
