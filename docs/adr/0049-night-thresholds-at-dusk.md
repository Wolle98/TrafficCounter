# ADR-0049: The night thresholds also apply at dusk

- **Status:** accepted
- **Date:** 2026-10-27
- **Stage:** 1
- **Extends:** [ADR-0028](0028-night-thresholds.md) (separate confidence thresholds for the night)

## Context

ADR-0028 lowered the thresholds for new tracks at night to 0.2 / 0.25; dusk (civil twilight) kept the day values 0.5 / 0.6 until it was measured. The switch `Tracker:NightThresholdsAtDusk` and the regression option `--dusk-thresholds day|night` make that measurement possible.

Measured on 2026-10-27 with the model tc-v6p on six annotated videos that contain dusk (906 vehicles):

| Dusk thresholds | Correct | Missed | Extra |
|---|---|---|---|
| day (0.5 / 0.6) | 881 | 25 | 2 |
| night (0.2 / 0.25) | 884 | 22 | 2 |

Four videos are unchanged; in two of them one or two vehicles fewer are missed. The macro F1 is the same in both cases.

## Options considered

1. Keep the day thresholds at dusk.
2. **Use the night thresholds at dusk as well.**

## Decision

Option 2. `Tracker:NightThresholdsAtDusk` is on by default; it can still be switched off in the configuration.

## Consequences

- Positive: in the twilight vehicles whose lights the model sees before their outline are tracked early enough, as at night.
- Negative: the effect is small (3 of 906 vehicles); lower thresholds could add extra counts in a dusk with much reflection, which the measured videos did not show.
