# ADR-0030: PostgreSQL on the NAS and synchronisation with SQL Server

- **Status:** accepted, extended by [ADR-0051](0051-day-notes-and-holidays.md) (holidays and notes are synchronised)
- **Date:** 2026-09-30
- **Stage:** 1, 4

## Context

The counter is to run permanently on a notebook ([ADR-0029](0029-computing-load.md)). As long as it writes into SQL Server Express on the development machine, that machine would always have to be switched on. A PostgreSQL server runs on the NAS (Synology) anyway. On the development machine, work continues with SQL Server; the previous data and the hand counts are there as well. Both databases are to be merged on demand.

## Decision

- **Two database systems, one model.**
  - `TrafficCounterDbContext` (SQL Server) and `PostgresTrafficCounterDbContext` (PostgreSQL) share the model; each has its own migrations (`Migrations/` and `Migrations/Postgres/`).
  - The service chooses with `Storage:Provider` (`SqlServer`/`PostgreSql`), matching the connection `ConnectionStrings:TrafficCounter`.
  - The tools use `TRAFFICCOUNTER_DB` or `TRAFFICCOUNTER_PG`.
- **Times:**
  - PostgreSQL stores `timestamp with time zone` in UTC, and Npgsql only accepts UTC. The PostgreSQL context therefore converts to UTC when writing.
  - SQL Server keeps local time with offset. Rows taken over from PostgreSQL are converted to local time.
  - The point in time is the same in both cases. Analyses by time of day calculate in local time, e.g. the quality history per hour, which groups in the program instead of in SQL for this reason.
- **Synchronisation** (`TrafficCounter.Sync sync`, `DatabaseSync`):
  - Every row of operating period, passage, minute statistic, event and drift measurement carries a `SyncId` (GUID). Cameras are matched by name.
  - Missing rows are inserted with new local keys.
  - Of a period that exists on both sides, the one with the newer `UpdatedAt` wins. The context sets it automatically on every change. This way heartbeat and end of a running period come across.
  - Hand-counted recordings have a fixed `SyncId` derived from the video name. A newer import replaces all passages of the recording on the other side.
  - **Deletions are not transferred.** Whoever corrects or deletes rows on one side has to repeat that on the other.
  - The synchronisation runs on request in one or both directions and may be repeated any number of times.
- **Access:**
  - The counter uses a role of its own, `trafficcounter`, as owner of its database, not the admin.
  - `TrafficCounter.Sync setup-postgres` creates role and database and applies the migrations.
  - Passwords are never in files or commands but in environment variables (`PGPASSWORD` for the admin, `TRAFFICCOUNTER_PG_PASSWORD` for the role) or in user secrets.

## Consequences

- Positive: the notebook writes directly to the NAS; the development machine can stay off. Both databases can be merged at any time. The analysis GUI can later run against either.
- Negative / risks:
  - Two migration branches have to be maintained with every schema change (`dotnet ef migrations add … --context PostgresTrafficCounterDbContext --output-dir Migrations/Postgres`).
  - Deletions are not synchronised.
  - The synchronisation loads all `SyncId`s per period. That is sufficient for years of data; for very large amounts of data a timestamp filter would be needed.
  - If the NAS is unreachable, the service buffers in the fallback file as before (ADR-0024).
- Follow-up work: the analysis GUI (stage 4) must support both database systems.

## Implementation note (2026-10-07)

The synchronisation also covers the hourly weather (`WeatherHour`, [ADR-0023](0023-weather-and-image-quality.md)), and `sync` applies pending migrations to both databases first. The analysis app supports both database systems.
