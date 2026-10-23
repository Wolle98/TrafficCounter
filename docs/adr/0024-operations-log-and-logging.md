# ADR-0024: Operations log in the database and debug logs

- **Status:** accepted
- **Date:** 2026-09-26
- **Stage:** 1
- **Extends:** [ADR-0010](0010-persistence-sql-server-ef-core.md), [ADR-0012](0012-runtime-worker-service.md), [ADR-0023](0023-weather-and-image-quality.md)

## Context

For the analysis it must be clearly determinable for every period which of the following cases applied:

1. The system was **off**, or the recording has gaps because the PC was off or the service crashed.
2. The system **ran but evaluated nothing**: camera disconnected, no frames, pane blind.
3. The system **ran and evaluated**, but no vehicle passed.

`OperatingPeriod` with a heartbeat (ADR-0010) only answers case 1. For troubleshooting in operation, detailed logs are also needed, but they do not belong in the database.

## Decision

### Database (for the analysis)

1. **`OperatingPeriod`: run time of the service**
   - Starts when the service starts, ends at an orderly shutdown.
   - Stored are the **end reason** (`Shutdown`, `Unclean`, `Error`) and the **nominal frame rate**.
   - **Detecting an unclean end:** at the next start, periods of the same camera that are still open are closed. The end is the last heartbeat, the reason `Unclean`, for example after a power failure or crash. This is also logged as an event.
2. **`MinuteStatistic`: one row per minute and camera while the service runs**
   - Content:
     - frames received, evaluated and dropped;
     - detections, passages;
     - state of the frame source (`Ok`, `Unavailable`);
     - quality level ([ADR-0023](0023-weather-and-image-quality.md));
     - brightness, contrast, sharpness, lighting conditions.

     Values that only later tasks provide stay empty until then.
   - Minutes without frames are **still written**, then with 0 frames and source `Unavailable`.
   - The minute rows double as the **heartbeat**: with every written minute, `OperatingPeriod.LastHeartbeatAt` is updated.
   - This way each case follows directly from the database:

     | Finding | Meaning |
     |---|---|
     | No row for the minute | case 1, system off |
     | Row with 0 evaluated frames or quality `Unusable` | case 2, ran but evaluated nothing |
     | Row with evaluated frames and 0 passages | case 3, ran and evaluated, no traffic |

   - In analyses, a minute counts as **covered** if at least 50 % of the frames expected from the nominal frame rate were evaluated and the quality is not `Unusable`. The threshold is adjustable in the analysis.
   - Passages are linked to the minutes by time, without a foreign key.
   - About 1,440 rows per day, which is negligible.
3. **`SystemEvent`: significant state changes**
   - Events: start, stop, unclean end detected, camera connected or lost, quality level change, configuration change, database unreachable or reachable again, errors.
   - Per event: time, severity, kind and a short text.
   - No debug details; the table serves as a chronicle for the analysis GUI.
4. **Resilience:**
   - Passages, minute rows and events go through the same buffered writer (ADR-0010).
   - If the database is unreachable, they go into a local JSONL file. As soon as the database is back, they are loaded automatically. Nothing is lost, and the minute rows from the downtime arrive afterwards too.

### Debug logs (for operation)

5. **Serilog** with three sinks:
   - **File:** daily rotation, level `Debug` by default, 14 days retention, at most 100 MB per file. Stored under `%ProgramData%\TrafficCounter\logs` for the service or `./logs` in console mode.
   - **Console:** `Information`.
   - **Windows event log:** `Warning` and above, only when running as a service.

   All levels are configurable.
6. **Structured logging** with properties such as `OperatingPeriodId`, `Camera`, `TrackId`:
   - At `Debug` level, every passage is logged with track ID, direction, class, position on the line and number of frames. In addition track start and end, stitching, reconnects, pipeline timings per minute and DB batches.
   - `Verbose` contains per-frame details and is meant for troubleshooting only.
7. **Privacy:** logs contain no images and no location data. Box coordinates and track IDs are not critical. The log files are outside both repos.

## Consequences

- Two new tables (`MinuteStatistic`, `SystemEvent`) and two new columns in `OperatingPeriod`. They come with the migration for the DB writer (roadmap 1.6).
- The analysis GUI (stage 4) can show coverage and gaps exactly and compute hourly/daily counts only over covered minutes or extrapolate them.
- The logs allow troubleshooting afterwards without storing images.

## Implementation note (2026-10-11)

A USB camera once kept delivering about one black frame per second for seven hours. The device stayed "OK" and the reads succeeded, so the source counted as up and the minutes were stored as measured, with 0 vehicles. Since then a `SourceWatchdog` judges windows of 120 s. If all brightness samples are black, or the capture rate is below a quarter of the learned normal rate, the source counts as stalled. The minutes are then stored as `Unavailable`, a `SourceLost` event with severity Error is written, and the camera is reopened (`IReopenableFrameSource`) after every stalled window. Existing data is corrected with `TrafficCounter.Sync mark-unmeasured`.

## Implementation note (2026-10-15)

An IP camera delivered 80–90 % of its frames for two hours, with more than 20 short interruptions and reconnects. Every minute was stored as measured, but a quarter of the vehicles were missing. The watchdog now also tracks a degraded stream: an interruption (no frames for 5 s), or a window below 90 % of the nominal rate, starts an episode that lasts until five healthy windows in a row (10 minutes). The rate check only applies to sources with a fixed rate (RTSP); a USB camera legitimately drops from 30 to 20 fps with the longer night exposure. Minutes of an episode get the new state `Degraded`, which the analysis treats as not measured like `Unavailable`. The start and end of an episode are written as `SourceDegraded` events. Rates per hour are computed only from the vehicles of measured minutes, so the vehicles of a degraded minute do not raise them.

## Implementation note (2026-10-17)

On a rainy night, raindrops on the window pane in front of the camera became bright, out-of-focus discs in the light of the street lamp and headlights. The camera counted about half of what a second camera counted, while stream and image quality values looked normal. A `PaneMonitor` now counts drop-like discs in one frame every 10 s in the dark (`RaindropDetector`: difference of Gaussians at the disc scale, round bright blobs at 640 px width). If the median of a 2-minute window reaches 35 (a dry night gives 10–20, the rainy one 70–80), the minutes count as `Degraded` until five clear windows in a row, like a degraded stream; start and end are written as `ViewObstructed` events. By day the drops had no visible effect on the counts, so the check runs only at dusk and night.
