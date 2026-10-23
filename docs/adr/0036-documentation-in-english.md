# ADR-0036: Documentation in English

- **Status:** accepted, point on the app user interface superseded by [ADR-0037](0037-multilingual-analysis-app.md)
- **Date:** 2026-10-07
- **Stage:** all
- **Supersedes:** the language rule for documentation in [ADR-0001](0001-use-adrs.md)

## Context

ADR-0001 set German for the documentation (roadmap, ADRs, guides) and English for code, identifiers, comments and commit messages. The public repo is meant to be readable internationally; German documentation next to English code splits the project into two languages and excludes most potential readers.

## Decision

- All documentation in the public repo is in **English**: README, `docs/` including the ADRs, tool help texts, scripts and the templates that the setup script writes into a new private repo.
- Existing documents were translated on 2026-10-07. The ADRs were translated faithfully; their content was not changed. Where the implementation has moved on since, a dated note was added instead of rewriting the decision.
- File names of documents and ADRs are English as well.
- The user interface of the analysis app stays German for now: it is the product used locally, not documentation. Making it multilingual would be a separate decision.
- Documents in the private repo may stay German.

## Consequences

- Positive: one language for code and documentation; the public repo is accessible to more readers.
- Negative: links to the old German file names (for example in bookmarks) no longer work.
