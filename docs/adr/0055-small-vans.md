# ADR-0055: Small vans – labelled as vans, tolerated as cars

- **Status:** accepted
- **Date:** 2026-10-28
- **Stage:** 3
- **Extends:** [ADR-0039](0039-labelling-rules.md) (labelling rules), [ADR-0054](0054-vehicle-classes-as-counted.md) (vehicle classes)

## Context

Vans are the weakest class: in the regression of 2026-10-28 over 3,155 vehicles only 107 of 180 vans were counted as vans, nearly all others as cars. Many of them are high-roof estates ("Hochdachkombi", for example Opel Combo, VW Caddy, Citroën Berlingo, Renault Kangoo, Fiat Doblò). They exist as panel vans without rear side windows and as passenger cars with windows and a rear bench; from the camera both look nearly alike. Whether such a vehicle is a car or a van is a matter of judgement even for the person counting by hand. Larger vans and minibuses (for example Ford Transit, Mercedes Sprinter, VW Transporter, Renault Trafic) are clearly vans.

## Options considered

1. Keep the evaluation strict: every small van counted as a car is an error.
2. **Mark small vans in the hand count and accept car or van for them; label them uniformly for training.**
3. Treat all car/van confusions as tolerable.

## Decision

Option 2.

- **Training labels:** high-roof estates are always labelled as `Van`, whatever their windows. One rule for all of them gives the model consistent examples; larger vans and minibuses are `Van` anyway.
- **Hand counts:** the Annotator marks a small van with the key `k` (or its own quick button); it is stored as class `SmallVan` in `.gt.csv` and read as `Van` with the mark. `t`/`T` jump to the next or previous van entry, so older counts can be sorted into small and large vans.
- **Evaluation:** a small van counted as a car counts as right; the regression reports how many there were. A large van counted as a car stays an error.
- **Counting** does not change: the counter knows no small vans, it reports car or van.

## Consequences

- Positive: the van score measures what matters, the large vans and minibuses, and the training gets one rule for the ambiguous vehicles.
- Negative: the 180 vans in the existing hand counts are unmarked until they are reviewed; until then the evaluation stays strict for them. Training labels of high-roof estates given as `Car` before this decision have to be corrected for the next model.
