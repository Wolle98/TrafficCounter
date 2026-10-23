# ADR-0010: Persistence in SQL Server Express with EF Core, individual events

- **Status:** accepted, extended by [ADR-0024](0024-operations-log-and-logging.md) (operations log, debug logs) and [ADR-0030](0030-postgresql-and-synchronisation.md) (PostgreSQL as a second database system)
- **Date:** 2026-09-26
- **Stage:** 1

## Context

MS SQL Server is a requirement; it is installed locally as the Express edition, which is free up to 50 GB per database. The analysis (stage 4) needs flexible aggregations, and stages 2, 3 and 5 add further attributes to the counting event.

## Options considered

- Granularity: **one event per vehicle** vs. pre-aggregated counts per minute or hour.
- Access: **EF Core** vs. Dapper/ADO.NET.
- Time: `datetime2` in UTC vs. **`datetimeoffset`**.

## Decision

- **Individual events** in `VehiclePassage` (see [architecture.md](../architecture.md)). With an estimated ≤ 10,000 vehicles per day, that is about 4 million rows per year, only a few hundred MB. Aggregates are views or, later, indexed or materialised hourly tables.
- The columns for direction, class and speed exist **from the start** (nullable). New stages therefore need no data migration, only filling.
- `datetimeoffset(3)` stores local time including the offset. This is convenient for analyses by time of day and unambiguous at the daylight saving time change.
- **EF Core** with code-first migrations for schema and access. Complex analyses in stage 4 may use raw SQL/views.
- Writing is **decoupled and buffered**: events go into a `Channel<T>` queue and are written in batches. If the DB is unreachable, writing is retried and, if necessary, spilled into a local JSONL file that is loaded later. This way no counts are lost and the pipeline does not block.
- `OperatingPeriod` with a heartbeat (every minute) documents when counting took place.

## Consequences

- SQL Server 2025 Express is installed as the default instance (`localhost`) with Windows authentication. The database is called `TrafficCounter`. A different connection string is set via `TRAFFICCOUNTER_DB` or the configuration.
- Direction and vehicle class are stored as `tinyint`. The class names are in the lookup table `VehicleClass`, the direction names per camera in `Camera`.
- Real database backups go into the private repo or onto the NAS, never into the public one.
