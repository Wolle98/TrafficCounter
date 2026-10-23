# ADR-0001: Record architecture decisions as ADRs

- **Status:** accepted, language rule superseded by [ADR-0036](0036-documentation-in-english.md)
- **Date:** 2026-09-26
- **Stage:** 0

## Context

The project is developed in five stages over a longer period. Otherwise the reasons behind decisions, such as "why ONNX instead of OpenCV DNN", get lost.

## Decision

- Every decision that is hard to reverse or affects several components gets an ADR in `docs/adr/` in MADR style (template: [`0000-template.md`](0000-template.md)).
- Numbers are consecutive and never reused. An ADR is not rewritten but **superseded** by a new one; the statuses then refer to each other.
- The index is in [`README.md`](README.md) and is updated with every ADR.
- Language: documentation (roadmap, ADRs) in **German**. Code, identifiers, comments and commit messages in **English**, so that the public repo stays readable internationally.

## Consequences

- Decisions are traceable and can be discussed in review (one pull request per ADR is possible).
- Some additional writing effort.
