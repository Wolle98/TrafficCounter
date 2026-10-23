# ADR-0003: Platform .NET 10 (LTS), C#, initially Windows x64

- **Status:** accepted, solution format superseded by [ADR-0021](0021-solution-format-sln.md)
- **Date:** 2026-09-26
- **Stage:** 0

## Context

Requirement: C# with a current .NET version. The target platform is initially Windows on x64; a later move (another PC, a mini PC) should remain possible. Currently only the .NET SDK 9.0 is installed.

## Options considered

1. .NET 9 (STS, support ends in November 2026)
2. **.NET 10 (LTS, support until November 2028)**

## Decision

- `net10.0`; only the GUI (stage 4) may use `net10.0-windows`.
- Solution in the new `.slnx` format, central package versions via `Directory.Packages.props`, shared settings via `Directory.Build.props` (Nullable, TreatWarningsAsErrors, LangVersion latest).
- Platform-specific parts (DirectML, Windows service) are encapsulated behind interfaces so that a Linux build stays possible later.

## Consequences

- The .NET 10 SDK has to be installed.
- An LTS support period suits a 24/7 service that is supposed to run for a long time without maintenance.
