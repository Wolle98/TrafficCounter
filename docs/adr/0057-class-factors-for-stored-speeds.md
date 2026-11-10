# ADR-0057: Class factors for speeds stored before the camera model

- **Status:** accepted
- **Date:** 2026-10-31
- **Stage:** 5
- **Extends:** [ADR-0056](0056-speed-from-camera-model-and-near-edges.md)

## Context

Since version 0.13.1 the main camera measures speeds on a 3D camera model from the camera-near box edges (ADR-0056). The speeds stored before were measured with the linear scale between the kerbs and the global factor ×1.08. ADR-0056 showed that this method depends on the vehicle class: the box centre of a long vehicle does not move with it, and the box top enters with a class width only on the camera model. Buses came out about a quarter too slow.

The stored speeds of the main camera since its start are therefore not comparable with the new ones per class. The counts are not affected.

The factor per class was measured on 33 recordings of the main camera from seven days (32 before its realignment, one after), each run twice through the regression: once with the linear scale as stored, once with the camera model of the camera's alignment at that time. For each passage measured by both, the ratio camera model / stored was taken; median and a bootstrap 90 % interval per class:

| Class | Passages | Before realignment | After | All | 90 % interval |
|---|---|---|---|---|---|
| Car | 1465 | 1.015 | 1.008 | 1.013 | 1.012–1.014 |
| Van | 71 | 1.031 | 1.021 | 1.025 | 1.020–1.035 |
| Truck | 23 | 1.052 | 1.042 | 1.052 | 1.024–1.061 |
| Bus | 12 | 1.386 | 1.360 | 1.380 | 1.358–1.418 |
| Agricultural | 2 | 1.123 | – | 1.123 | 0.995–1.252 |
| Motorcycle | 14 | 0.964 | 0.970 | 0.967 | 0.953–0.979 |
| Bicycle | 94 | 0.957 | 0.956 | 0.956 | 0.953–0.963 |
| E-scooter | 25 | 0.952 | 0.941 | 0.948 | 0.945–0.954 |

Both alignments give the same factors within their intervals.

## Options considered

1. **Leave the stored speeds as they are** and mark the change of method in the analysis. Bus speeds stay a quarter too low in every period that reaches back before 0.13.1.
2. **Recompute every passage** from the recordings. There are recordings only for a few hours.
3. **Multiply the stored speeds of the classes the box geometry hits most by their measured factor**, with a backup of the original values.

## Decision

We choose option 3, because the factors are stable across recordings and alignments, and only a factor can reach the stored passages without recordings.

- Scaled classes and factors: **Bus ×1.38, Truck ×1.05, Van ×1.03**, the "all" medians rounded.
- Not scaled: cars (+1.3 %, within the accuracy of the method), agricultural vehicles (two passages, no reliable factor), two-wheelers (−3 to −5 %, not part of this decision).
- Range: passages of the counter (`Source = Counter`) of the main camera's counting machine from its start up to the switch to 0.13.1. Speeds of a second camera copied into the same database are not touched.
- Tool: `TrafficCounter.Sync scale-speeds` changes both databases and writes the old speeds per database and `SyncId` to a backup file first. The backup files stay in the private repository.

## Consequences

- Positive: Speeds per class before and after 0.13.1 can be compared; bus speeds are no longer a quarter too low.
- Negative / risks: A factor corrects the mean of a class, not the single passage; a single stored bus speed can still be off by several percent. The factors come from daylight recordings; night passages may differ.
- Follow-up work: Decide whether the two-wheeler speeds are scaled as well. Restoring is possible from the backup files.
