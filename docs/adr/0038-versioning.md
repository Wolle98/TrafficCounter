# ADR-0038: One version for all programs, raised with every deployment

- **Status:** accepted
- **Date:** 2026-10-11
- **Stage:** all

## Context

Only the counting service had a version, and it stayed at 0.1.0 although the service changed a lot since operation started. The operating periods and the log store the version. Because the build appends the commit hash ("0.1.0+9ca4ee7…"), the exact code state could be traced, but not at a glance: which period ran with the watchdog, which one still with the old drift check?

## Options considered

1. Keep 0.1.0 and rely on the commit hash.
2. Version derived from git automatically (tags plus commit count, e.g. MinVer or Nerdbank.GitVersioning).
3. One version set by hand in `Directory.Build.props`, raised with every deployment, with a changelog.

## Decision

Option 3. All programs (service, app, tools) share the version in `Directory.Build.props`. It follows Semantic Versioning with major 0 during development:

- **Minor** (0.x.0): new features, or changes that affect the stored data or its meaning.
- **Patch** (0.x.y): fixes and small changes without new features.

The version is raised before a deployment to the counting machine. The changes go into `CHANGELOG.md`, and the commit gets a tag `v<version>`. The build keeps appending the commit hash, so a deployment from an untagged commit can still be told apart.

A tool package (option 2) was not chosen: it adds a dependency, and deployments happen by hand anyway, so setting the number then is no extra step.

## Consequences

- Positive: the operations page and the log show at a glance which version counted when; the changelog explains what changed between two periods.
- Negative: raising the number can be forgotten; the commit hash still identifies the code then.
- Follow-up: none.
