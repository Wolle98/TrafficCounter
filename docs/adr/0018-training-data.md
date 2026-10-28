# ADR-0018: Training data – public datasets plus own images, semi-automatic labelling

- **Status:** accepted, extended by [ADR-0023](0023-weather-and-image-quality.md) (weather, backlight, image quality); step 2 (public datasets) not taken, see the addendum of 2026-10-28
- **Date:** 2026-09-26
- **Stage:** 3 (preparation from 1)

## Context

An own model is trained for the target classes from [ADR-0013](0013-vehicle-classes.md): car, van, truck, bus, motorcycle/scooter, bicycle/e-scooter, agricultural. Labelling only own images is laborious, and rare classes such as bus or motorcycle hardly occur. Public datasets help but have two problems:

- **Different perspective:** many datasets are taken from the driver's perspective (dashcam). Our camera, in contrast, looks down at the street from an elevated, oblique position.
- **Licences:** many traffic datasets are released for non-commercial purposes only.

## Datasets considered

The licences are stated to the best of our knowledge and **must be checked against the original source before use**.

| Dataset | Perspective | Relevant classes | Licence (to be checked) |
|---|---|---|---|
| UA-DETRAC | elevated traffic camera | car, bus, van, others | non-commercial (CC BY-NC-SA) |
| MIO-TCD | traffic cameras, also at night | car, pickup, work van, single-unit truck, articulated truck, bus, motorcycle, bicycle | probably non-commercial |
| Open Images V7 | mixed | Car, Van, Truck, Bus, Motorcycle, Bicycle | labels CC BY 4.0, images CC BY 2.0 |
| COCO | mixed | car, truck, bus, motorcycle, bicycle | labels CC BY 4.0; basis of the pre-trained models |
| Roboflow Universe | varies | varies | per dataset; quality varies |

## Decision

1. **Training in steps:**
   1. Pre-trained COCO model (Ultralytics YOLO).
   2. Further training on public data from a **traffic camera perspective** (UA-DETRAC, MIO-TCD, Open Images), whose classes are mapped to our target classes. The mapping table is a file in `tools/training/`.
   3. Fine-tuning with **a few hundred own images**, mixed from day, dusk, night and rain.
2. **Semi-automatic labelling:** the current model produces proposals that only need to be corrected in CVAT or Label Studio. The export and import script is in `tools/training/`.
3. **Classes without public data**, above all e-scooters, scooters and tractors, are covered specifically with own images. This is what the time-limited data collection mode from [ADR-0005](0005-privacy.md) is for, with pixelation before storing.
4. **Licence hygiene:**
   - Public repo: only scripts, class mappings and dataset configurations, no images.
   - Private repo: downloaded datasets or references to them, the trained weights and a `datasets/SOURCES.md` with source, version and licence of every dataset used.
   - Models trained with non-commercial data are **not published**.
5. **Evaluation:** the test set consists exclusively of own images not used for training, separated into day and night. Public data stays out of the evaluation because it does not reflect our camera situation.

## Consequences

- With about 80 % less labelling effort compared with purely manual labelling, stage 3 is realistic.
- Training happens locally on the RTX 3060 Ti (8 GB) with Python and Ultralytics. That is sufficient for YOLO-s/m at 640 px.
- External datasets are documented with versions so that training runs are reproducible.

## Addendum 2026-10-28: own images only

Step 2 was not taken. The own models (`tc-v…`) are fine-tuned from the COCO weights (`yolo11s.pt`) directly on own images from the data collection mode and from hand counts ([ADR-0026](0026-targeted-collection-of-training-samples.md), labelling rules [ADR-0039](0039-labelling-rules.md)); with some 3,200 own images (`tc-v6p`), public data from other cameras was not needed. Without public datasets there is no `datasets/SOURCES.md` either; it becomes necessary again as soon as one is used. The evaluation follows point 5: images taken while a hand-counted recording of the IP camera ran stay out of the training; these recordings are the test set.

