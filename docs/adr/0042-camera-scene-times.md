# ADR-0042: Day/night scene times of the IP camera follow sunrise and sunset

- **Status:** accepted
- **Date:** 2026-10-23
- **Stage:** 2
- **Supplements:** [ADR-0028](0028-night-thresholds.md) (night settings)

## Context

The NC400 switches between a day scene (short exposure, 1/500 s) and a night scene ("low illumination", 1/100 or 1/150 s) by a schedule with one entry per month (ISAPI `displayParamSwitch`, mode `month`: night, day, backlight, night). Fixed monthly times cannot follow the sun: on 22 and 23 October the day scene started at 07:30, half an hour before sunrise. The image got abruptly darker (brightness 55 → 35, contrast 37 → 18) and on the rainy morning the camera counted 2 instead of 34 vehicles in 20 minutes. Each change of daylight saving time shifts sunrise and sunset by another hour. Adjusting the camera by hand every few weeks is not acceptable for the user. The camera only works reliably with full and half hours in this schedule.

## Decision

- A `CameraSceneWorker` in the counting service (`CameraScenes:Enabled`) checks the camera's schedule once a day (03:05 and at start).
- From the site's latitude and longitude it computes sunrise and sunset of the day (`SceneSchedule`): day scene at sunrise plus 30 minutes, rounded to the nearest half hour; night scene at sunset, rounded down to the half hour.
- It changes only the entry of the current month: the end of the morning night range and the start of the evening night range. A range in between (the user's backlight scene) is kept and only moved if it would collide. Mode, scenes and all other image settings stay untouched.
- The camera is reached over ISAPI with the stream's credentials (digest authentication). Before a change the old schedule is saved next to the service's data; afterwards it is read back to check it.
- `CameraScenes:Apply` false (report only) logs what would be set without writing; it is used first.

## Consequences

- Positive: the scene switch follows the sun without manual work, including the changes of daylight saving time.
- Negative: the service writes to the camera. A wrong schedule would affect image quality, not the counting itself; the backup and the read-back limit the risk.
- Only cameras with this ISAPI resource are supported; other cameras leave the option off.
