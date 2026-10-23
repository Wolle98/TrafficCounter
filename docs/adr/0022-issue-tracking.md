# ADR-0022: Issue tracking on GitHub, strictly separated into public and private

- **Status:** accepted, points 4 (commits), 6 and 7 superseded by [ADR-0045](0045-public-snapshots.md)
- **Date:** 2026-09-26
- **Stage:** all
- **Extends:** [ADR-0002](0002-repository-split.md), [ADR-0005](0005-privacy.md)

## Context

Larger problems such as design flaws, bugs affecting the count data or stability problems should be tracked traceably. The public repo will be published later. Then its issues, comments, attachments, their edit history and cross-references are public too. None of it may allow conclusions about:

- location, street, neighbourhood;
- people, vehicles, number plates;
- the private data and habits of the operator.

## Decision

1. **Scope:**
   - The **roadmap** contains planned work.
   - **Issues** capture unplanned, larger problems.
   - **ADRs** record decisions.
   - **Commits** close issues (`Fixes #n`).

   An issue is worthwhile if at least one of these applies:
   - count data is affected, including data already stored;
   - an ADR assumption turns out to be wrong;
   - the problem cannot be fixed within the current task or needs investigation;
   - stability or performance in continuous operation.

   Small things fixed in the same work step get no issue.
2. **Where does an issue belong? The test:** could the text, an attachment or a link help someone determine the location, people, vehicles or the operator's surroundings, or does it refer to private files? **If in doubt, private.**
   - **Public (`TrafficCounter`):** only purely technical descriptions. Reproduction uses synthetic data, unit tests, generic descriptions ("vehicle is lost for more than 1 s when occluded") or publicly licensed sample videos.
   - **Private (`TrafficCounterPrivate`):** everything with real images, videos, location details, camera and mounting situation, site configuration, measurements from real operation, real count results or datasets.
3. **Split problems:** if a problem has both parts, **two issues** are created. The public one describes the technical core neutrally, the private one the details.
4. **Cross-references only in one direction and without links:**
   - Public issues, pull requests, commits, ADRs and code **never mention the private repo or its issues**.
   - Private issues may refer to public ones, but **only as plain text** ("public issue 12"), not as `#12`, `Wolle98/TrafficCounter#12` or a URL. This way no automatic event appears in the timeline of the public issue.
   - Commits in the public repo refer only to public issues.
5. **Attachments:** images and videos are **never** attached to public issues, not even pixelated. In private issues they are allowed; a reference to the file in the private repo is better.
6. **Correcting mistakes:** if something private ends up in the public repo, the issue is moved to the private repo immediately with `gh issue transfer`, or the comment is deleted. As long as the public repo is still private, no trace has become public.
7. **Publication checklist:** before `TrafficCounter` becomes public, all issues, pull requests, comments, commit messages and files of the entire history are checked for violations of points 2 to 5.
8. **Structure:**
   - Both repos have the same kind of labels, the private one additionally `site`, `recording`, `dataset`, and milestones "Stage 1" to "Stage 5".
   - The issue templates of the public repo require confirming via checkbox that no private information is included. Blank issues without a template are disabled there.
9. **Claude:**
   - Claude proposes issues and only creates them after approval.
   - Before creating, Claude assigns the issue according to point 2 and states the assignment.
   - If in doubt, Claude creates it in the private repo.

## Consequences

- Some extra effort for problems with both parts (two issues).
- Public issue tracking stays free of any reference to the location even after publication.
- The name of the private repo only appears in the public repo in the documentation of the repo split (ADR-0002, `CLAUDE.md`). That is not critical because it allows no conclusions about the location.
