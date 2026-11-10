# ADR-0058: Class factors for stored two-wheeler speeds

- **Status:** accepted
- **Date:** 2026-10-31
- **Stage:** 5
- **Extends:** [ADR-0057](0057-class-factors-for-stored-speeds.md)

## Context

ADR-0057 scaled the stored speeds of buses, trucks and vans of the main camera before version 0.13.1 to the camera model and left two-wheelers open. The same measurement on 33 recordings shows two-wheelers about 3 to 5 % slower on the camera model than stored, with narrow intervals and the same value before and after the camera's realignment (table in ADR-0057). Their narrow box is near the class width the camera model assumes (0.6 m, motorcycles 0.8 m), while the linear scale with ×1.08 was fitted to cars.

## Options considered

1. Leave the two-wheelers as stored; the change of method stays visible in their time series.
2. Scale them with their measured factor like the classes in ADR-0057.

## Decision

We choose option 2, because the factors are as stable as those in ADR-0057 and a step of 4–5 % at 0.13.1 would otherwise show up as a change in cycling speeds.

- Factors, the "all" medians of ADR-0057: **Bicycle ×0.956, E-scooter ×0.948, Motorcycle ×0.967**.
- Range and tool as in ADR-0057, with a backup of the original values per class.

## Consequences

- Positive: All classes except cars and agricultural vehicles are now on the level of the camera model; cars differ by +1.3 %, inside the accuracy of the method.
- Negative / risks: As in ADR-0057, a factor corrects the class mean, not the single passage.
- Follow-up work: none.
