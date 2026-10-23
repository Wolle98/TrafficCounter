# ADR-0004: License AGPL-3.0

- **Status:** accepted
- **Date:** 2026-09-26
- **Stage:** 0

## Context

The public repo is to be published on GitHub. The most capable and most easily trainable detectors (Ultralytics YOLOv8/YOLO11 and successors) are licensed under AGPL-3.0. Whoever integrates them into a project has to license that project under AGPL as well, unless an enterprise license is bought.

## Options considered

1. MIT/Apache-2.0 with Apache-licensed models (YOLOX, RT-DETR, D-FINE, RF-DETR)
2. **AGPL-3.0 with the Ultralytics ecosystem**

## Decision

AGPL-3.0 for the public repo. A `LICENSE` file with the full AGPL-3.0 text is added. Dependencies must be AGPL-compatible. OpenCvSharp (Apache-2.0), ONNX Runtime (MIT) and EF Core (MIT) are.

## Consequences

- Ultralytics training, export and models can be used without restriction.
- Whoever passes the project on as a network service has to provide the source code. For a private hobby project this is not critical.
- The private repo is not affected (not published).
