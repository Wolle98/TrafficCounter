# ADR-0051: Notes on days and imported holidays explain unusual traffic

- **Status:** accepted
- **Date:** 2026-10-27
- **Stage:** 4
- **Extends:** [ADR-0014](0014-gui-technology.md) (analysis app), [ADR-0023](0023-weather-and-image-quality.md) (weather import), [ADR-0030](0030-postgresql-and-synchronisation.md) (synchronisation)

## Context

Traffic changes for reasons the counter cannot see: a building site that narrows the road to one lane, a public holiday, school holidays. Without a hint in the analysis app such days look like counting errors, and comparisons with "usual" days mislead.

## Options considered

1. Notes in a text file of the private repository.
2. **Notes in the database, entered in the analysis app or with `TrafficCounter.Sync`; holidays imported automatically.**

For the holidays:

1. A file per year in the private configuration, maintained by hand.
2. Public holidays computed locally, school holidays by hand.
3. **Both from OpenHolidays (https://openholidaysapi.org): open data, no API key, public and school holidays per region.**

## Decision

Notes: table `DayNote` with start, end (exclusive), text, `SyncId` and `UpdatedAt`. A note without times covers whole days. The analysis app gets a page "Notes" to add, change and delete them; `TrafficCounter.Sync add-note`, `list-notes` and `delete-note` do the same in PostgreSQL. Deleting only marks a note, so the sync passes the deletion on; of two copies the newer one wins. The app thereby writes to the database for the first time, but only this table.

Holidays: table `CalendarEntry`, one row per public holiday or school holiday span with the OpenHolidays id, both names (country language and English) and the region. The service imports last, this and next year once a day (`HolidayWorker`) when `TrafficCounter:Holidays:Subdivision` (ISO 3166-2, private configuration) is set; `TrafficCounter.Sync holidays` imports on demand. Only the region code leaves the machine. The sync copies the entries by their id.

The app shows notes and holidays on the overview, as a list and per day on the "Period" page, and lists the holidays of the year on the "Notes" page.

## Consequences

- Positive: unusual days carry their explanation; holidays need no maintenance.
- Negative: the app needs write access to its database. Regional holidays below state level (e.g. one city's holiday) come in as OpenHolidays lists them for the state.
- Follow-up: the weekly and daily patterns could leave holidays and noted days out of the "usual" values.
