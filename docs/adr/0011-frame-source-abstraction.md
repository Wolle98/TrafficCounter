# ADR-0011: Abstraction of frame sources, "latest frame" strategy

- **Status:** accepted
- **Date:** 2026-09-26
- **Stage:** 1

## Context

IP cameras (RTSP), USB webcams and, for tests and development, video files should be supported. RTSP streams buffer, break off and deliver fluctuating frame times.

## Decision

- `IFrameSource` delivers `Frame { Mat Image; DateTimeOffset Timestamp; long Sequence }`.
- Implementations based on OpenCvSharp `VideoCapture`:
  - `RtspFrameSource`: FFmpeg backend, TCP transport, automatic reconnect with backoff. A separate capture thread keeps only the latest frame; discarded frames are counted and logged as a metric.
  - `UsbFrameSource`: MSMF/DirectShow by device index; resolution and FPS configurable.
  - `FileFrameSource`: plays back either in real time or as fast as possible. Timestamps are computed from the frame position, so regression tests are deterministic.
- Timestamps are set on reception; for stage 5 it will be checked whether the stream PTS are more accurate.

## Consequences

- The pipeline is independent of the source; regression tests run with files.
- If the OpenCV FFmpeg path turns out to be unstable with certain cameras, an implementation using FFmpeg directly (e.g. `Sdcb.FFmpeg`) can be added.
