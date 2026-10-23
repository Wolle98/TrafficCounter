# ADR-0021: Classic solution format `.sln` instead of `.slnx`

- **Status:** accepted
- **Date:** 2026-09-26
- **Stage:** 0
- **Supersedes:** the point "solution in the new `.slnx` format" of [ADR-0003](0003-platform-dotnet.md)

## Context

ADR-0003 had chosen the new XML-based solution format `.slnx`. At the request of the project owner, the classic `.sln` format is used again. It is supported by all IDEs, versions of Visual Studio and third-party tools.

## Decision

- The solution is called `TrafficCounter.sln`, with the project folders `src` and `tests`. `TrafficCounter.slnx` is dropped.
- New projects are added with `dotnet sln TrafficCounter.sln add --solution-folder <src|tests> <path>`.
- The remaining points of ADR-0003 (.NET 10, central package management, `Directory.Build.props`) apply unchanged.

## Consequences

- Maximum compatibility with tools.
- The `.sln` format is harder to read and prone to merge conflicts with parallel changes (GUIDs). For a one-person project this is not critical.
- The CI (`.github/workflows/build.yml`) and the documentation refer to `TrafficCounter.sln`.
