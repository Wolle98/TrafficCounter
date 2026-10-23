# ADR-0006: Object detection with YOLO via ONNX Runtime – from stage 1

- **Status:** accepted, extended by [ADR-0020](0020-local-traffic-situation.md) (evaluation band, wide model input)
- **Date:** 2026-09-26
- **Stage:** 1 (basis for 2, 3, 5)

## Context

Stage 1 would only need "moving object" and no classification. Stage 3, however, needs a detector with classes. Difficult conditions: shadows, headlights at night, rain, trees moving in the wind, pedestrians, closely following vehicles.

## Options considered

1. **Background subtraction** (OpenCV MOG2/KNN + contours): light and fast, but sensitive to shadows, light and occlusion. It separates neither pedestrians nor closely following vehicles and would have to be replaced for stage 3 anyway.
2. **OpenCV DNN module** with YOLO ONNX: only one library, but GPU use on Windows only with a self-built OpenCV plus CUDA, and newer model architectures are not always supported.
3. **ONNX Runtime** with a YOLO ONNX export (Ultralytics YOLO11 or newer): official NuGet package, interchangeable execution providers (CPU, DirectML, CUDA, OpenVINO), good performance.

## Decision

Option 3, **from stage 1**.

- Start with the pre-trained COCO model (`yolo11s` or `yolo11n`, input 640 px, possibly cropped to the ROI); the classes car, truck, bus, motorcycle, bicycle and optionally person are kept.
- The model is chosen as an ONNX file via configuration. The detector reads the class names from the ONNX metadata, so the own model from stage 3 can be used without code changes.
- Pre- and post-processing (letterbox, NMS) are implemented in C# and tested against the Ultralytics reference.
- OpenCV remains responsible for frame sources, pre-processing and visualisation ([ADR-0016](0016-opencvsharp.md)).

## Consequences

- This makes stage 1 more work but yields robust counts. Stages 2 and 3 build directly on it.
- Python + Ultralytics is needed once for the ONNX export and later for training (`tools/training`).
- The computing load is higher than with background subtraction, see [ADR-0007](0007-hardware-acceleration.md).
