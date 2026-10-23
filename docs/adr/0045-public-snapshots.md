# ADR-0045: Public repository as checked snapshots of a private working repository

- **Status:** accepted
- **Date:** 2026-10-23
- **Stage:** 0
- **Extends:** [ADR-0002](0002-repository-split.md); supersedes points 4 (commits), 6 and 7 of [ADR-0022](0022-issue-tracking.md)

## Context

Before publication, the review of the whole history found details about the site in older commits: an observed bus timetable, the facade direction, real direction names, a first name. The current state was cleaned, but the history keeps them. Rewriting the history (`git filter-repo`) would change every commit hash, and GitHub keeps the old commits reachable through pull request references, so the repository would have to be deleted and recreated anyway. Every later slip in a commit would raise the same problem again.

## Options considered

1. Rewrite the history once and recreate the repository; afterwards work in public as before.
2. Squash everything into one commit in the existing repository.
3. **Keep working in a private repository and publish only checked snapshots** of released versions to a separate public repository.

## Decision

Option 3.

- **`TrafficCounterWork`** (private, the former `TrafficCounter` repository) is where all work happens: full history, tags, CI, Dependabot and the earlier issues.
- **`TrafficCounter`** (public) receives one commit per version (ADR-0038): `tools/publish/Publish-PublicSnapshot.ps1` takes the tracked files of the version tag, leaves out `.github/dependabot.yml`, checks every text line against the patterns in the private repository (`config/publish-denylist.txt`) and against the direction names, coordinates and weather station of the private site configuration, and stops at the first hit. A clean snapshot is committed as `TrafficCounter <version>` with the CHANGELOG section as message and tagged; pushing is a separate, explicit step.
- **Issues** are reported and kept in the public repository. The issues of the working repository stay there; code, docs and the CHANGELOG refer to ADRs instead of issue numbers, because numbers of the working repository mean nothing in public.
- **Pull requests** in the public repository are carried over into the working repository by hand and arrive in public with the next snapshot.
- The pattern list grows with every finding. It lives in the private repository, since the patterns themselves describe the site.

## Consequences

- Positive: nothing of the working history can become public; a mistake in a commit is caught before the snapshot or fixed before the next one.
- Positive: no history rewrite, no force push, all tags and issue references of the working repository stay valid.
- Negative: the public history is coarse (one commit per version), and outside contributions need a manual round trip.
- Negative: the pattern check only finds what is listed; descriptions of the site still need a human eye when a version adds documentation.

## Implementation note (2026-10-23)

Edits made directly in the public repository (for example a file uploaded through the GitHub web editor, like the README video) would be overwritten by the next snapshot. The script pulls the public clone and stops if there are commits after the last snapshot tag, until they have been carried over into the working repository and `-PublicEditsCarriedOver` confirms it.
