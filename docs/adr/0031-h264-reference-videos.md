# ADR-0031: Convert reference videos to H.264 with ffmpeg afterwards

- **Status:** accepted; for recordings by the service extended by [ADR-0032](0032-recordings-by-the-service.md) (written as H.264 by the hardware encoder right away)
- **Date:** 2026-10-05
- **Stage:** 1

## Context

`record` writes reference videos with OpenCV. Without additional libraries, OpenCV on Windows can only write MPEG-4 Part 2. One hour in 1080p is then 1.4–7.5 GB, depending on image content and noise. That does not fit Git LFS (at most 2 GB per file) and fills the disks quickly.

The following was tested:
- H.264 directly via OpenCV's FFmpeg binding needs Cisco's OpenH264 library. For licensing reasons it has to be downloaded from Cisco itself, and it compresses only moderately.
- H.264 and HEVC via Windows Media Foundation work, but with a fixed, very high bit rate. The files become about 5 times larger than with MPEG-4, and this cannot be adjusted via OpenCV.

## Options considered

1. Stay with MPEG-4 and only record the evaluation band (`--band`).
2. Put OpenH264 next to the tool.
3. Record as before and then convert to H.264 with an installed ffmpeg.

## Decision

We choose option 3. ffmpeg is an external, optional program. If it is missing, the video stays MPEG-4.
- `record` converts automatically after recording (`--compress auto|on|off`).
- `compress --video <file|folder>` converts existing recordings.
- The default is `libx264` with CRF 26. Key frames come every 30 frames so that the annotator can seek frame-accurately. Each input frame yields exactly one output frame (`-fps_mode passthrough`).
- The original is only replaced when the new file has the same number of frames. The file name stays the same, so that `.frames.csv`, `.gt.csv`, `.json` and `.site.json` still match.

Measured on two counted videos with `yolo11s-640x192`, 15 fps and motion detection:

| Video | MPEG-4 | H.264 CRF 26 | Accuracy MPEG-4 | Accuracy H.264 |
|---|---|---|---|---|
| Midday, 60 min | 5.6 GB | 0.95 GB (17 %) | 92.7 % | 92.7 % (same errors) |
| Night 9 pm | 1.4 GB | 0.31 GB (21 %) | 90.5 % | 90.5 % |

On an image section from dusk, the PSNR was 43 dB compared with MPEG-4. Targeted seeks in the H.264 video returned the same frames as sequential reading.

## Consequences

- Positive: videos become about 5 times smaller without measurable effect on counting. One hour usually fits below the LFS limit of 2 GB.
- Positive: with `--encoder h264_qsv` (Intel Quick Sync) or `h264_nvenc` (NVIDIA) it is fast on weaker machines too. On the notebook with an i5-1334U, libx264 needed about twice the video duration, Quick Sync about a sixth.
- Negative: the conversion is lossy, a second time after MPEG-4. The images are still good enough for later training (stage 3). Whoever needs it with little loss uses `--quality 18` or `--compress off`.
- Follow-up work: convert existing recordings as soon as they are no longer open in the annotator.

## Note (2026-10-07)

With low-contrast backlight images, the double lossy compression (MPEG-4, then H.264) did cost detections: on a backlight recording the service counted 86 % live, the regression on the video only 71–76 %. The comparison above (daylight, night) was not affected. The service now records directly to H.264 ([ADR-0032](0032-recordings-by-the-service.md)).
