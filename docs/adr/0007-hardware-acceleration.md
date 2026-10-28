# ADR-0007: Hardware acceleration via DirectML with CPU fallback

- **Status:** accepted, extended by [ADR-0029](0029-computing-load.md) (OpenVINO build for Intel graphics) and [ADR-0041](0041-tensorrt-backend.md) (TensorRT and CUDA for NVIDIA)
- **Date:** 2026-09-26
- **Stage:** 1

## Context

The target hardware is open: Windows x64, with a dedicated GPU (currently RTX 3060 Ti), with an integrated GPU (Intel/AMD) or none at all. Existing GPUs should be used without separate code for each vendor.

## Options considered

1. CUDA/TensorRT execution provider: fastest, but NVIDIA only; CUDA/cuDNN installation needed.
2. OpenVINO execution provider: very good on Intel CPU/iGPU, but not on NVIDIA or AMD.
3. **DirectML execution provider** (`Microsoft.ML.OnnxRuntime.DirectML`): any DirectX 12 GPU (NVIDIA, AMD, Intel including iGPU), no driver packages needed besides the graphics driver.
4. Windows ML (successor of DirectML, chooses execution providers dynamically): future-proof, but ties to the Windows App SDK and Windows 11 24H2+.

## Decision

Option 3 as the default. Configuration `Detection:ExecutionProvider` = `Auto | DirectML | Cpu` (later extensible with `Cuda`, `OpenVino`).
`Auto` tries DirectML on the first adapter and falls back to the CPU on errors; the result is logged. The adapter index is configurable in case both an iGPU and a dGPU are present.

## Consequences

- One code base for all GPUs. On NVIDIA, DirectML is somewhat slower than TensorRT but more than sufficient for YOLO-s at 25 fps.
- DirectML is in maintenance mode at Microsoft; further development happens in Windows ML. Thanks to the encapsulation in `IObjectDetector`/`InferenceSessionFactory`, a later switch stays local. A follow-up ADR will be written when needed.
- Performance guideline: on a pure CPU, `yolo11n` at about 10–20 fps is realistic. That is enough for counting but tight for speed measurement (stage 5).
