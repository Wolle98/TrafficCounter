# ADR-0052: Standing vehicles start over, a stop gives no speed

- **Status:** accepted
- **Date:** 2026-10-28
- **Stage:** 3
- **Extends:** [ADR-0015](0015-speed-measurement.md) (speed measurement) and [ADR-0020](0020-local-traffic-situation.md) (local traffic situation)

## Context

The review of hand-counted videos and of the stored speeds showed two faults caused by vehicles that stood still:

- **Track takeovers.** A vehicle parked or waiting in the view keeps its track for minutes. When another vehicle drives past it closely, the tracker may hand the parked vehicle's track to the moving one, together with all class votes collected while it stood. A street sweeper was stored as a bus and an SUV as a truck this way.
- **Speeds through a stop.** A vehicle that waited in the measuring zone (at the crossing, behind a parking car) gets a line fitted through its waiting time. All 8 stored speeds below 3 km/h checked against the recordings were wrong: none was a vehicle driving that slowly. A stop also breaks the free-flow condition of ADR-0020.

## Options considered

1. Leave the tracker and the speed fit as they are and clean the data afterwards.
2. Drop tracks that stood for some time.
3. **Start a track's class votes, first position and speed samples over when it moves off after standing, and give no speed to a passage with a stop in the measuring zone.**

## Decision

Option 3. Option 2 would lose the vehicles that really waited and then drove over the line.

- **Standing reset:** a track that stayed `StandingSeconds` (2 s) within `StandingRadiusRatio` (0.25 box heights) of one point was standing. When it moves off, its class votes, frame votes, first timestamp, first distance and speed samples start over. The radius scales with the box, so it fits near and far lanes.
- **Only before the crossing:** what a track did up to the counting line stays with it. A first version reset after the crossing too; a pedestrian pausing behind the line then lost the speed that marks it as no vehicle, and the regression got two extra counts.
- **Stop rule:** a passage that moved less than `StopMetersPerSecond` (1 m/s) over any `StopSeconds` (1 s) of its time in the speed zone stopped there and gets no speed.
- **Pseudo-vehicles:** the walking-pace rules (ADR-0048, ADR-0050) use the mean speed over the whole observation when a stop leaves the passage without a speed. Otherwise a slow e-scooter or pedestrian with a short pause would be counted.
- **Clearing old values:** `TrafficCounter.Sync clear-speeds --below <km/h> [--from] [--to]` removes stored speeds below a limit in both databases and keeps the passages. It was run once with 3 km/h (361 speeds).
- **Settings:** `StandingResetSeconds` in the site configuration, 0 switches the reset off. The regression takes `--standing-reset <s>`.

## Consequences

Measured with the regression over 47 hand-counted videos (3,155 vehicles), against the version without reset and with the ground truth before the small-van review (ADR-0055):

| | correct | missed | extra | Van F1 | Truck F1 | Bus F1 | macro F1 |
|---|---|---|---|---|---|---|---|
| without reset | 3,081 | 71 | 19 | 0.735 | 0.928 | 0.979 | 0.796 |
| reset, also after the crossing | 3,081 | 71 | 21 | 0.777 | 0.958 | 1.000 | 0.808 |

- Positive: vans, trucks and buses are classified better, because waiting time no longer outvotes the drive.
- Positive: the two extra counts came from the reset after the crossing; with the reset only before the crossing both videos count as without reset.
- Positive: the 25 measured speed test drives are unchanged (mean absolute error 6.9 %); free drives do not stand.
- Negative: passages with a stop in the zone have no speed. They were no free drives anyway, but the share of passages with a speed drops slightly.

## Addendum 2026-10-28: measured with the corrected ground truth, two more defaults

The full regression was repeated with the ground truth after the small-van review (ADR-0055). Two further changes were measured in the same run and are defaults since:

- **Votes weighted by box width** (`WeighVotesByWidth`, regression `--weigh-votes none|width`): each frame's class vote counts with its box width relative to the widest box of the track, so boxes cut off at the image edge count less than the full side view.
- **Lower thresholds for two-wheelers** (`Tracker:TwoWheelerHighConfidence` 0.3, `Tracker:TwoWheelerNewTrackConfidence` 0.35, regression `--two-wheeler-confidence 0.3/0.35|off`): bicycles, e-scooters and motorcycles start and continue tracks with lower confidence. At night the lower night thresholds stay in force.

A third candidate was rejected: counting a track that ends shortly after the line only if it got at least 2 % of the image width beyond it (`--min-end-beyond 0.02`) missed 2 vehicles more and saved only 1 extra count.

| | correct | missed | extra | Van F1 | Truck F1 | Bus F1 | Motorcycle correct | E-scooter correct | macro F1 |
|---|---|---|---|---|---|---|---|---|---|
| without reset | 3,081 | 71 | 19 | 0.817 | 0.943 | 0.979 | 20 | 60 | 0.809 |
| reset before the crossing | 3,081 | 71 | 19 | 0.854 | 0.972 | 1.000 | 20 | 60 | 0.820 |
| plus width weighting and two-wheeler thresholds | 3,084 | 68 | 19 | 0.894 | 0.972 | 1.000 | 19 | 62 | 0.822 |

- The reset before the crossing leaves the counts exactly as they were and only improves the classes.
- Weighting and two-wheeler thresholds together find 3 vehicles more without a single extra count, and 12 vans more are classified right; one motorcycle less is.
