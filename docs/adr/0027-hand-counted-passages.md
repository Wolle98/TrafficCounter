# ADR-0027: Hand-counted passages in the database

- **Status:** accepted
- **Date:** 2026-09-30
- **Stage:** 1, 4

## Context

During a reference recording (`record`), the recording occupies the camera and the counting service does not run. This leaves a gap in the database, although the traffic of that time is later counted completely by hand (`.gt.csv`, [reference-videos.md](../reference-videos.md)). These hand counts are even more accurate than the automatic count, especially for vans and e-scooters.

## Decision

Hand-counted recordings are taken over into the same tables and marked:

- **`OperatingPeriod.Source` and `VehiclePassage.Source`:** `0` counter, `1` hand count (`DataSource`). In addition `SourceVideo`, the file name of the recording, and for passages `VideoOffsetSeconds`, the position in the video.
- **Each recording becomes an operating period of its own** with the source hand count, from its first to its last frame. Analyses thus see the time as covered, with reference quality.
- **Values of a passage:**
  - direction from the calibration stored with the video (`.site.json`);
  - day/dusk/night from the sun position;
  - the class as assigned by hand.
  - The fields only the counter knows stay empty: position on the line, track, confidence, trailers, speed.
- **Import:** `TrafficCounter.Annotator import <video.mp4> …`. Importing again replaces the entries of the same video, so corrected counts can be added later.
- **No double counting:** if a recording overlaps a counter period, the import rejects it.

## Consequences

- Positive: reference recordings fill gaps instead of creating them, and daily totals become more complete. Because video and position are stored, every hand-counted passage can be found again in the video.
- Negative / risks: analyses that measure counter performance (detections, quality, fps) must filter by `Source = Counter`. Hand-counted periods have no minute statistics.
- Follow-up work: the analysis GUI (stage 4) shows the source and can filter it.
