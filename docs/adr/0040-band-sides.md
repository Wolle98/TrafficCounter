# ADR-0040: Left and right edge of the evaluation band

- **Status:** accepted
- **Date:** 2026-10-13
- **Stage:** 1
- **Supplements:** [ADR-0020](0020-local-traffic-situation.md) (evaluation band as a horizontal strip over the full image width)

## Context

The evaluation band was a strip between two rows across the whole image width. With a wide-angle IP camera this has three drawbacks:

- The image edges are strongly distorted. Vehicles there are compressed, and the speed scale measured near the image centre does not hold.
- Disturbances lie mostly at the edges: reflections of the room in the pane, a lens flare beside a headlight, backlight effects.
- The detector input has a fixed aspect ratio (640×192 = 3.3:1). A band of 1920×435 px (4.4:1) is scaled down more than necessary to fit, so every vehicle, especially bicycles on the far lane, becomes smaller in the input.

## Options considered

1. Keep the full width and cover the edges with ignore zones.
2. A left and a right edge of the band, set in the calibration tool like the top and bottom edge.

## Decision

Option 2. `Site:BandLeft` and `Site:BandRight` (fractions of the image width, default 0 and 1) narrow the band. Ignore zones (option 1) are no substitute. They drop detections by their ground point, but the image edges would still be fed to the detector and would still shrink the vehicles.

- **Calibration tool:** key `w` sets the left and right edge with two clicks, `W` restores the full width. The tool shows the band size and its aspect ratio next to the model's (from the model file name).
- **Room beside the line:** the counting needs a stretch of road on both sides of the counting line. A passage is emitted 15 % of the image width past the line, and speeds are measured within 20 % of it. If less than that plus 5 % remains between the line and a band edge, the calibration tool warns.
- **Recordings keep the full width.** Only the analysis uses the narrowed band, so it can be changed afterwards and compared on the same video.
- Ignore zones, static zones (drift check) and the counting line stay in whole-image coordinates.

## Consequences

- Positive: distorted and disturbed edges are left out, and a band close to 3.3:1 makes vehicles about a third larger in the detector input. Less to compute.
- Negative: a vehicle is visible for a shorter stretch, so tracks are shorter. The warning in the calibration tool guards against cutting off too much.
- Follow-up: compare counts of the same recording with full and narrowed band.
