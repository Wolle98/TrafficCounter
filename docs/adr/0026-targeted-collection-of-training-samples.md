# ADR-0026: Targeted collection of training samples in operation

- **Status:** accepted
- **Date:** 2026-09-29, decided 2026-10-06
- **Stage:** 3

## Context

For the own model (stage 3b, [ADR-0018](0018-training-data.md)) we need images from our own camera. Most useful are examples that the current model recognises wrongly or uncertainly. Randomly collected images mostly show well-recognised cars.

First data from trial operation confirm this:

- **The class rarely wavers.** `ClassConfidence` is the vote share of the winning class over all frames of a track. It is below 0.6 for only just over 1 % of the passages.
- **Trucks are the problem case.** The "truck" passages average only 0.74, and there are conspicuously many of them for a 30 km/h street. These are probably mostly vans wavering between car and truck.
- **Some errors show no uncertainty at all.**
  - A van recognised as a car throughout has 1.00.
  - E-scooters are not detected at all; the COCO model only sees the person.
  - A tractor was only detected after it had already crossed the counting line. It was therefore not counted, and the COCO model never recognises it as agricultural anyway.

So far [ADR-0005](0005-privacy.md) only allows storing images explicitly and for a limited time. A mode that automatically stores individual images over days goes beyond that. It therefore has to be decided separately and limited tightly.

## Options considered

1. **Only reference videos** (`record`, ADR-0005 point 3), recorded by hand, with images extracted from them.
   - Simplest in terms of data protection.
   - Rare cases (tractor, bus at night, e-scooter in the rain) are caught only by chance and with a lot of effort.
2. **Threshold on `ClassConfidence`**, as originally planned: store images when the value is below 0.6.
   - Only captures wavering classes, i.e. about 1 % of the passages.
   - Systematic errors and undetected objects slip through.
3. **Targeted collection with several triggers, single images instead of videos and strict limits.**

## Decision

We choose option 3 as a **collection mode**. It extends ADR-0005 point 3. Decided on 2026-10-06: collection mode yes, the blanket anonymisation is sufficient for now, and the anonymised images are collected for training.

### Implementation (as of 2026-10-06)

- **Detecting people:** for anonymising, the detector additionally reports people. It reads them from the same model output, i.e. without a second model run, and only while the collection mode is active. People are neither counted nor tracked nor stored.
- **Ignore zones** (driveways with parked vehicles, [ADR-0025](0025-ignore-zones.md)) are pixelated completely. The counting deliberately does not detect their vehicles, so the box rule does not reach their number plates.
- **Two-wheelers:** above every bicycle and motorcycle box, the area where the rider's head is located is pixelated as well, in case the person was not detected.
- **Images** are only taken from frames on which the detector ran, so that the person boxes are current.
- **Image at the counting line:** only within a narrow area around the line, so that encoding does not slow down counting.
- **Storage:** the default is `datasets/inbox/<date>/<time>-<track>/` with `entry.jpg`, `line.jpg`, `exit.jpg` and `sample.json` (trigger, class, direction, boxes as labelling proposal). On the counting machine without a private repo, a folder of its own is set in the configuration.
- **At night** the trigger "unsure detection" is dropped: with the night thresholds ([ADR-0028](0028-night-thresholds.md)) almost every vehicle would be unsure, and the daily limit would be used up before the morning.
- **Not yet implemented:** the trigger "person faster than about 12 km/h" for e-scooters, because people are not tracked. E-scooters are collected for now via the random sample and "recognised late".

### Triggers (per counted passage or per track)

| Trigger | Purpose |
|---|---|
| `ClassConfidence` < 0.8 | class wavers, e.g. vans between car and truck |
| mean detector confidence < 0.5 | detected uncertainly, e.g. night, rain, occlusion |
| rare classes always: truck, bus, motorcycle, agricultural | enough examples for rare classes (guideline ≥ 300 per class, ADR-0013) |
| slow tracks: clearly more frames per passage than usual, e.g. more than twice the moving median | tractors and other slow special vehicles that the COCO model recognises as car or truck |
| track only starts near the counting line or beyond it | late-detected vehicles of unusual shape; such tracks are collected even if they are not counted |
| `TrailerCount` > 0 | combinations for labelling "towing vehicle with trailer in one box" |
| tracks of the model class `person` crossing the line faster than about 12 km/h | candidates for e-scooters (not counted, only collected) |
| random sample of 2 % of all passages | also finds confidently wrong cases (van as car) and provides the test set |

The thresholds are in the configuration. The reason for the selection is stored with each example, so it can later be analysed which trigger delivers useful examples.

### What is stored

- **Three single images per track:** on entering the evaluation band, at the counting line and on leaving. No video sequence.
- **Only the evaluation band**, at full resolution, as JPEG. The model input is 1280×384; more resolution than the band has is not needed.
- **Metadata as JSON**, without image data in the database:
  - the detected boxes of all objects with model class and confidence, as a proposal for semi-automatic labelling (ADR-0018 point 2);
  - track, direction, time, lighting conditions, trigger.
- **Storage:** `datasets/inbox/<date>/` in the private repo, found via `TRAFFICCOUNTER_PRIVATE`. Destinations in the public repo are rejected, as with `record`.

**Technique:** images of a track are only selected when its passage is emitted. Until then the service keeps at most three JPEG-encoded band images per active track in memory, a few 100 KB in total. Images not selected are discarded without ever reaching the disk. Encoding and writing run asynchronously and must not slow down counting. If the queue is full, examples are discarded, never frames of the counting.

### Limits (privacy, ADR-0005)

1. **Off by default.** Switched on only via configuration, with an end date: `SampleCollection:Until`, at most 30 days ahead. Afterwards the mode switches itself off.
2. **Daily limit**, by default 200 tracks, i.e. at most 600 images per day.
3. **Anonymise before writing**, not only before checking in:
   - **Number plates:** with a number plate detector. Which one is open, see below. Until one is available, the lower third of every vehicle box is pixelated. This hardly affects classification because shape and size are preserved.
   - **Faces:** the upper fifth of every person box is pixelated. Cyclists remain recognisable as cyclists.
4. **Retention period:** examples not annotated are deleted automatically after 90 days. Annotated ones move into the training set and fall under ADR-0018.
5. **Transparency:**
   - At start-up the service logs clearly that the collection mode is active, including the end date.
   - A `SystemEvent` records start and end.
   - The minute statistics count the collected examples.

*(Technical design decision, not legal advice. Permanently storing images of public street space is legally more delicate than pure counting. The limits above are meant to keep the intrusion as small as possible but do not replace a legal review.)*

## Consequences

- **Positive:**
  - Labelling time goes into the cases that actually improve the model.
  - Rare classes accumulate along the way.
  - The random sample also provides an unbiased test set (ADR-0018 point 5).
- **Negative / risks:**
  - The privacy intrusion is larger than before, hence the tight limits.
  - More code in the service: image buffer per track, selection, anonymisation, writer.
  - The blanket pixelation can also hide parts that matter for the class, for example with motorcycles.
- **Follow-up work:**
  - Anonymisation as a separate, tested module (`TrafficCounter.Vision`).
  - Export script `datasets/inbox` → CVAT or Label Studio in `tools/training/`.
  - Tests for selection, daily limit, end date and retention period.
  - The line in the roadmap section stage 3b.

## Open points

- **Number plate detector:** check a publicly available model, including its licence, or train an own one on a few hundred examples. Until then the blanket pixelation applies.
- **Night:** whether night images are good enough for labelling is decided with the night test (ADR-0017). The mode may collect only the random sample at night.
- **Volume:** after two weeks, check whether the triggers use the daily limit sensibly or the thresholds need adjusting.

## Addendum 2026-10-07: daily limit spread over the day

On the first day the limit was used up by 11 am, leaving nothing for evening, dusk, night and rain. The daily limit is therefore spread over the day: at any time of day, 10 % of it (`BurstShare`) plus the elapsed part of the day is available. On the counting machine the limit is 300 for now.

## Addendum 2026-10-11: bicycles as a rare class, copy to the private repository

After five days only 37 of 1,249 samples were bicycles, and none were e-scooters. Yet these are the classes the own model is meant to improve most. Bicycles (class `Bicycle`, which includes e-scooters) now count as a rare class like trucks and buses. `tools/deploy/Get-TrainingSamples.ps1` copies new samples into the private repository as one tar archive. The images go to Git LFS; about 5 GB by the end of the collection fit into the free 10 GiB.

## Addendum 2026-10-12: samples from hand counts

The service only collects what the model tracks. Bicycles and e-scooters that it misses therefore never become samples, although they are the most valuable ones. `TrafficCounter.Regression --extract-samples <class>` cuts them from the hand-counted recordings instead. It writes three images per passage (0.6 s before, at and after the line), anonymised like the service's samples, with the detector's boxes as proposals, into `datasets/inbox` in the same format. The first run gave 95 bicycles and e-scooters, at day, dusk and night.

## Addendum 2026-10-18: only the rider's head is pixelated

The space above a bicycle or motorcycle was pixelated for every two-wheeler, which covered most of the rider's body. Since ADR-0039 the rider belongs to the two-wheeler's box, and the model sees unpixelated riders in operation, so the pixelated body widened the gap between training and live images. The space above a two-wheeler is now pixelated only if no person was detected on it; otherwise the head part of the person box is pixelated as before. Samples collected earlier stay as they are.
