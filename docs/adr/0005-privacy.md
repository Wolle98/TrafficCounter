# ADR-0005: Privacy – no image storage in normal operation

- **Status:** accepted, point 3 extended by [ADR-0026](0026-targeted-collection-of-training-samples.md) (anonymised training samples) and [ADR-0032](0032-recordings-by-the-service.md) (reference recordings by the service) and [ADR-0053](0053-speed-clips.md) (clips of conspicuous speeds)
- **Date:** 2026-09-26
- **Stage:** all

## Context

The camera covers public street space with people and number plates. Under the GDPR and the German Federal Data Protection Act (BDSG), video surveillance of public areas by private individuals is problematic. The processing becomes much less critical if no images are stored, only anonymous counting events are produced and the image section is limited to what is necessary. *(This is a technical design decision, not legal advice.)*

## Decision

1. **Normal operation:** images are only processed in memory. Only metadata such as time, direction, class and speed is stored, but no images, crops, number plates or personal features.
2. **Data minimisation:** an ROI mask limits processing to the road. Footpaths and neighbouring properties are blacked out in the camera itself via privacy masking where possible. The resolution is only as high as needed for detection.
3. **Recording and data collection mode** (reference videos, training data for stage 3): can only be enabled explicitly via configuration and for a limited time. The only destination is the private repo. Before checking in, faces and number plates are made unrecognisable automatically (tool in `tools/`). The debug window shows live images but stores nothing.
4. The service logs clearly at start-up when a recording mode is active.

## Consequences

- Recounting from videos afterwards is not possible in normal operation. Quality assurance therefore relies on the reference videos.
- An additional tool for pixelating is needed before training data is created (stage 3b).
