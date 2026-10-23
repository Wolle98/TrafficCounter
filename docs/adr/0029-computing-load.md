# ADR-0029: Adjustable computing load for weaker hardware

- **Status:** accepted
- **Date:** 2026-09-30
- **Stage:** 1

## Context

So far the counter ran on the development machine with an RTX 3060 Ti: every frame at 30 fps, `yolo11s` with an input of 1280×384, about 16 ms per frame. In continuous operation the graphics card should stay free, because it heats up the room and is needed for other things. The counter should also run on a notebook with integrated graphics. Without a graphics card, an i5-13600KF manages about 16 fps on the CPU at full input size, a notebook considerably less.

Which combination best balances detection rate and load depends on the target hardware. It is to be measured there, not decided in advance.

## Decision

Six independent knobs, all configurable. The defaults correspond to the previous, most accurate behaviour:

1. **Processing frame rate** `Processing:MaxFps`: frames above the rate do not go through processing at all.
2. **Model input size:** models with a fixed size (e.g. 640×192) or a dynamic model whose size is set by `Detector:InputWidth`/`InputHeight`. The vehicles are large in the image, so a smaller input loses little. In the first functional test, 640×192 detected the same vehicles as 1280×384.
3. **Motion detection** `Processing:MotionGate` (`MotionGate`): every frame of the band is downscaled, converted to greyscale and compared with the previous frame. If too little changes outside the ignore zones, model, tracking and counting do not run for this frame. After motion the model keeps running for 1.5 s, without motion at least every 5 s. Waiting vehicles are kept, because the tracker works time-based and re-associates their tracks at the next run.
4. **Model variant** via `Detector:ModelPath`, e.g. `yolo11n`.
5. **Intel OpenVINO** as a second build (`-p:InferenceBackend=OpenVino`, `ExecutionProvider: OpenVino`, `OpenVinoDevice: GPU|NPU|CPU|AUTO`), plus **INT8 quantisation** (`tools/training/quantize_model.py`). Only the convolutions are quantised, because a shared INT8 scale for box coordinates and class scores in the output tensor wipes out the class scores.
6. **Camera frame rate** `Source:Usb:Fps`, so that fewer frames have to be decoded.

The regression takes the same settings (`--model`, `--max-fps`, `--motion-gate`, `--provider`, `--openvino-device`, `--threads`) and reports the computing load per video. This allows comparing accuracy and load directly on the target hardware.

## Consequences

- Positive: the counter can be adapted to the hardware without changing the code. The knobs multiply; 1 to 3 together save roughly a factor of 20–50.
- Negative / risks:
  - Motion detection may trigger more often in rain, snow or with changing headlights. That costs computing time, but no accuracy.
  - OpenVINO and DirectML each bring their own `onnxruntime.dll` and are only possible as separate builds.
  - INT8 and smaller models may detect rare and small objects less well. Check with the regression before use.
- Follow-up work: measure combinations on the target hardware and record the configuration used there. ADR-0007 (DirectML) remains valid for the standard build.

## Implementation note (2026-10-07)

On the counting notebook (Intel Iris Xe) the service runs the OpenVINO build on the GPU with `yolo11s-640x192`, `MaxFps` 15 and the motion gate switched on (`tools/deploy/appsettings.Production.json`).
