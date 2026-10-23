# ADR-0002: Split into a public and a private repository

- **Status:** accepted, extended by [ADR-0045](0045-public-snapshots.md)
- **Date:** 2026-09-26
- **Stage:** 0

## Context

The code is meant to be publishable. Images, videos, training data, site configuration and real count data, on the other hand, show the personal surroundings and must not become public.

## Options considered

1. One repo with a `.gitignore` for private data: high risk that something gets checked in by accident at some point.
2. Git submodules (private inside public): public clones would see a reference to an inaccessible repo.
3. **Two independent repos, loosely coupled via paths and environment variables.**

## Decision

Option 3.

- `TrafficCounter` (public) contains code, documentation, tools, example configuration (`*.example.json`) and synthetic test data.
- `TrafficCounterPrivate` contains the site configuration, recordings, datasets, models, DB backups and reports. Large binary files go into **Git LFS**.
- Coupling: environment variables `TRAFFICCOUNTER_CONFIG` (configuration file) and `TRAFFICCOUNTER_PRIVATE` (root of the private repo, among other things for regression tests). If they are missing, tests that need private data are skipped instead of failing.
- Secrets (RTSP password, connection string with password) are in **neither** repo but in `dotnet user-secrets` or environment variables.

## Consequences

- The public repo is free of private data by construction.
- Additional protection: the `.gitignore` of the public repo excludes videos, images outside `docs/` and `*.onnx`/`*.pt`.
- Mind the GitHub LFS quota (1 GB free); alternatively keep raw videos only locally or on the NAS.
