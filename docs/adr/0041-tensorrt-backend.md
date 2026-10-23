# ADR-0041: TensorRT and CUDA backend for NVIDIA graphics cards

- **Status:** accepted
- **Date:** 2026-10-23
- **Stage:** 3
- **Supplements:** [ADR-0007](0007-hardware-acceleration.md) (DirectML), [ADR-0029](0029-computing-load.md) (OpenVINO build)

## Context

The detector runs the ONNX model through ONNX Runtime: DirectML on any DirectX 12 GPU, OpenVINO on Intel graphics (counting notebook). On NVIDIA cards TensorRT is usually several times faster than DirectML, especially in half precision (FP16). The PC with an RTX card runs the offline tests of new models against the hand counts; one round over four recordings takes about an hour and a half. A counting machine with an NVIDIA card could also use it.

## Decision

- A third build variant, `-p:InferenceBackend=Nvidia`, references `Microsoft.ML.OnnxRuntime.Gpu.Windows` instead of the DirectML package (each ships its own `onnxruntime.dll`). It gets its own `bin-nvidia`/`obj-nvidia` folders like the OpenVINO build.
- Execution providers `TensorRT` and `Cuda`. With `Auto` the NVIDIA build tries TensorRT (with CUDA for nodes TensorRT does not take), then CUDA, then the CPU.
- TensorRT computes in FP16 by default (`TensorRtFp16`) and caches the built engines (`TensorRtCachePath`, default `trt-cache` next to the model), so the minutes of engine building only happen on the first start of a model.
- The CUDA, cuDNN and TensorRT libraries are not shipped. `NvidiaLibraryPaths` lists folders that are put in front of `PATH`; on the PC these are the libraries of the PyTorch (CUDA, cuDNN) and TensorRT Python packages in `tools/training/.venv`.
- First use: offline tests (regression) on the PC. A new backend for counting is only used after the regression shows the same counts as DirectML.

## Consequences

- Positive: faster offline tests; counting on NVIDIA hardware possible without code changes.
- Negative: a third build variant; the NVIDIA libraries are large (several GB) and their versions must match ONNX Runtime (CUDA 12, cuDNN 9, TensorRT 10).
- FP16 can change detections slightly; the regression decides whether it is acceptable.
