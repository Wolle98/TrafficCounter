# ADR-0016: OpenCvSharp4 as the OpenCV binding

- **Status:** accepted
- **Date:** 2026-09-26
- **Stage:** 1

## Context

OpenCV is needed for camera access, image pre-processing, homography and debug visualisation.

## Options considered

1. **OpenCvSharp4** (Apache-2.0): API close to C++/Python, actively maintained, ready-made Windows runtime packages (`OpenCvSharp4.runtime.win`) including FFmpeg.
2. Emgu CV: dual licence GPL/commercial, the runtime packages are partly paid.
3. SkiaSharp/ImageSharp plus own FFmpeg: no computer vision functionality.

## Decision

OpenCvSharp4. `Mat` objects are consistently released via `using`, because native memory otherwise leaks in continuous operation. A 24-hour memory stability test is part of the stage 1 acceptance.

## Consequences

- The OpenCV version is tied to the OpenCvSharp releases.
