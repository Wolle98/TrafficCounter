# ADR-0053: Short videos of passages with a conspicuous speed

- **Status:** accepted
- **Date:** 2026-10-28
- **Stage:** 5
- **Extends:** [ADR-0005](0005-privacy.md) no. 3 and [ADR-0032](0032-recordings-by-the-service.md) (recordings by the service)

## Context

Some stored speeds are implausible, for example a truck at 50–55 km/h. Whether the vehicle was fast or the measurement was wrong (a box growing at the image edge, a track taken over from another vehicle) can only be decided on the video. Reference recordings cover fixed slots; a conspicuous passage happens at any time and is usually not in one.

## Options considered

1. Plan more reference recordings and hope that conspicuous passages fall into them.
2. Record continuously and delete everything that is not needed.
3. **Keep the last seconds in memory and write a short clip only for passages whose speed matches a rule.**

## Decision

Option 3.

- **Enabling:** `SpeedClips:Until` (at most 30 days ahead) and at least one rule. Like the other recording modes the clips contain personal data, so the mode is enabled explicitly and for a limited time, and the service logs a warning at start-up and for every clip.
- **Rules:** `SpeedClips:Rules` is a list of `MinKmh` (inclusive), `BelowKmh` (exclusive, optional) and `ExceptClasses`. A passage is recorded if any rule matches. To start with: 45–55 km/h for all classes except cars, and from 55 km/h for all classes.
- **Recording:** the service keeps the evaluation band of the processed frames (the `Recording` band) of the last seconds JPEG-compressed in memory. When a matching passage is emitted, it writes the frames from `SecondsBefore` (4 s) before to `SecondsAfter` (3 s) after the crossing. Encoding and writing run in threads of their own; if they fall behind, frames are missing in the clip, never in the counting. The clip gets the same companion files as a reference recording (`.frames.csv`, `.json` with the passage, `.site.json`), so the Annotator and the regression open it like one, and it is converted to H.264 with low priority.
- **Limit:** at most `DailyLimit` (50) clips a day, so a wrong scale cannot fill the disk.
- **Destination:** the folder `speed` in the recording folder on the counting machine. `tools/deploy/Get-SpeedClips.ps1` copies new clips into the private repository. Destinations in the public repository are rejected.

## Consequences

- Positive: every conspicuous speed can be checked on video, without anyone on site and without a counting outage.
- Negative: while enabled, the service holds about 15 s of band images in memory (some 50 MB) and JPEG-encodes every processed frame on a thread of its own.
- Negative: the clips are not anonymised, like reference recordings; they belong to the private repository only and should be deleted once checked.
- Follow-up: the rules can be changed as the measurement improves.
