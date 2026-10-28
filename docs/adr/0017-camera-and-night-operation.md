# ADR-0017: Camera and night operation – start with a USB webcam, night test as decision point

- **Status:** accepted, extended by [ADR-0023](0023-weather-and-image-quality.md) (weather, backlight, image quality); switch path taken, see the addendum of 2026-10-28
- **Date:** 2026-09-26
- **Stage:** 0, 1 (effects up to 5)

## Context

The counter must also work at night, when the street is lit only by street lamps. The camera is on the first floor **behind a window pane**, about 7–8 m from the street. At night, it is mainly the camera that determines the detection quality:

- **Exposure time:** automatic exposure extends the exposure time at night to 1/25 s or longer. Vehicles become streaks of light. The exposure time must therefore be **limitable manually** (guideline ≤ 1/250 s). More noise is less harmful to detection than motion blur.
- **Amount of light:** it depends mainly on the sensor area and the aperture. The pixel count is secondary: with the same sensor area, 2K instead of 4K brings little at night because we downscale to 1080p anyway.
- **Behind the pane:** IR illumination reflects on the pane and is useless. Lamps in the room reflect as well.

Options checked (as of 2026-09):

| Camera | Sensor / aperture | Manual exposure | Price approx. | Assessment |
|---|---|---|---|---|
| UGREEN FineCam Lite 4K (USB) | 8 MP, size not stated (~1/2.8"), f/1.8, 70° | unclear | ~€40 | cheap entry, questionable at night |
| USB industrial camera with Sony IMX462 | 1/2.8" for low light, large pixels | yes (UVC) | ~€60–85 | no suitable variant with IR cut filter on Amazon.de |
| Reolink CX410 (PoE) | 1/1.8", f/1.0, 89° | yes | ~€90 | favourite, but discontinued |
| Reolink CX810 (PoE) | 1/1.8", f/1.0, 4K | yes | ~€105–120 + PoE injector | successor, currently over budget |
| Tapo C325WB | 1/1.88", f/1.0 | no | ~€80 | no control over the exposure time |

## Decision

1. **Start with the UGREEN FineCam Lite 4K** as prototype camera: operation in **1080p at 30 fps in MJPEG format**, via USB on the counting PC.
2. **Camera probe as the first tool** (brought forward to stage 1). It checks:
   - which settings the camera actually allows under Windows via OpenCV (MSMF/DirectShow): exposure, autofocus, gain, formats, fps;
   - whether the values can be fixed;
   - live image showing exposure time, image brightness and actual fps;
   - as a last resort it opens the driver's settings dialog (DirectShow `CAP_PROP_SETTINGS`).
3. **Night test as decision point,** if possible within the return period. The camera stays if
   - autofocus can be switched off or the focus stays stable on the street,
   - exposure can be limited to ≤ 1/100 s without the image becoming useless,
   - the frame rate does not drop below 20 fps at night,
   - vehicles in the night reference videos are recognisable in the debug overlay; the stage 1 acceptance criterion is ≥ 85 % at night.
4. **Switch path:** if the test fails or a good camera becomes available more cheaply (e.g. on Black Friday), we switch to an IP camera. Requirements:
   - sensor ≥ 1/1.8", aperture ≤ f/1.2;
   - **exposure time manually adjustable**, autofocus fixed or fixed focus;
   - RTSP stream with ≥ 25 fps, resolution 1440p or 1080p selectable;
   - IR illumination and spotlight can be switched off;
   - power via PoE injector or 12 V power supply, as there is no PoE switch;
   - candidates: Reolink CX810, CX410 if available again.

   The switch only affects the configuration ([ADR-0011](0011-frame-source-abstraction.md)). Line and ROI have to be recalibrated.
5. **Mounting behind the pane:**
   - place the lens as close to the pane as possible;
   - shield it against reflections from the room with a black hood;
   - no room light behind the camera.
   - Advantage of the webcam: it has no IR illumination.
6. **Day and night profile in the software:**
   - The program switches between two sets of settings: camera settings, confidence thresholds and optional contrast enhancement (CLAHE).
   - Switching is based on the mean image brightness, with the sun position (from the site coordinates) as a plausibility check.
   - Whether contrast enhancement helps is measured on the night videos, not assumed.
7. **Store lighting conditions:** each passage gets the lighting conditions at the time of measurement (`LightingCondition`: unknown/day/dusk/night). This way detection losses at night can be distinguished from a real drop in traffic in the statistics. The column is added by migration with the implementation of the day and night profile.

## Consequences

- The counting PC must be near the window: USB up to 5 m, beyond that an active extension. A later IP camera removes this constraint.
- The 70° field of view shows about 13–17 m of street. Articulated lorries do not fit completely into the image; for counting this is not critical, classification becomes somewhat harder.
- Night suitability is a deliberately accepted risk. The night test is therefore at the very beginning of stage 1.

## Addendum 2026-10-28: switch path taken

Since 2026-10-09 the counter runs with an IP camera, an Annke NC400, via RTSP ([ADR-0011](0011-frame-source-abstraction.md), roadmap 1.13). It meets the requirements of point 4, gets its power from a PoE injector and switches its day and night scenes itself ([ADR-0042](0042-camera-scene-times.md)). The UGREEN webcam keeps counting on the PC as a second camera for comparisons, into a database of its own. The night test of point 3 thus decided against the webcam as the only camera.

