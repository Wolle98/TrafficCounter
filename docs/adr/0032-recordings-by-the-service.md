# ADR-0032: Reference recordings by the running service

- **Status:** accepted
- **Date:** 2026-10-06
- **Stage:** 1, 3

## Context

So far reference videos are recorded by `TrafficCounter.CameraProbe record`. The tool needs the camera for itself, so the service has to be stopped in the meantime. For one hour of recording, one hour of counting is missing. In addition, someone has to be at the machine, which suits events that can only be planned roughly (rain, snow, dusk) poorly.

On top of that: if counting continues during the recording, the service counts exactly the video that is later counted by hand. That gives a direct comparison under real operating conditions, with the same frame rate, load and configuration.

[ADR-0005](0005-privacy.md) no. 3 only allows a recording mode explicitly via configuration and for a limited time. No. 4 requires the service to log an active recording mode clearly.

## Options considered

1. As before: stop the service, record with `record`.
2. The service records itself, controlled by a schedule.

## Decision

We choose option 2.

- **Enabling:** recordings are only possible if `Recording:Enabled` is `true` in the (private) configuration. The service then logs a warning at start-up.
- **Schedule:** which time slots are recorded is in `schedule.json` in the recording folder. Each slot has a fixed start with date, a duration of at most 60 minutes and tags. There are no recurring recordings. The service re-reads the file every 30 seconds, so a recording can be scheduled without a restart.
- **Recording:** the service writes the frames it receives from the camera anyway, at the full camera frame rate. Writing runs in a thread of its own with a bounded queue. If the writer cannot keep up, frames are missing in the video, never in the counting. The same companion files as with `record` are created (`.frames.csv`, `.json`, `.site.json`).
- **Afterwards** the video is converted to H.264 in an ffmpeg process with low priority ([ADR-0031](0031-h264-reference-videos.md)), by default with Intel Quick Sync.
- **Destination** is a local folder on the counting machine, by default `%ProgramData%\TrafficCounter\recordings`. From there the videos go into the private repository or to the PC for counting. Destinations in the public repository are rejected.

`record` remains available for recordings without a running service.

## Consequences

- Positive: recordings without a counting outage, even without anyone on site. With foreseeable weather the schedule is simply set generously, and unsuitable recordings are deleted.
- Positive: the automatic count of the same hour is in the database. A comparison with the hand count needs no regression run.
- Negative: more load on the counting machine during the recording (copying and MPEG-4 encoding, then the H.264 conversion) and disk space for the MPEG-4 original until the conversion (about 2–8 GB per hour).
- Negative: the service can now store images in principle. This is secured by the explicit enabling, time-limited slots and the warnings in the log.

## Addendum 2026-10-06: straight to the hardware encoder

MPEG-4 lost up to 18 % of the frames at dusk on the notebook, because noisy frames are expensive to encode. The service therefore now pipes the raw frames straight into ffmpeg with a hardware encoder (`Recording:Encoder`, default `h264_qsv`; `h264_nvenc` for NVIDIA). The graphics unit encodes, the processor only copies; there is no intermediate file and no later conversion. In a test with Quick Sync no frame was lost. With `libx264` (processor) the service writes Motion JPEG during the slot and converts it to H.264 afterwards; without ffmpeg it falls back to MPEG-4.

## Addendum 2026-10-07: frames from the capture thread

The recorder got its frames from the counting loop. When a detector run was slow (up to 0.2 s on the notebook after a quiet phase), the camera's frames were dropped before the loop saw them, and the video got gaps of up to 0.6 s. The recorder now observes every captured frame on the capture thread (`IObservableFrameSource`), so the video is complete regardless of the counting. Video files as a source keep the old path.
