# ADR-0012: Runtime model worker service (console and Windows service)

- **Status:** accepted, extended by [ADR-0024](0024-operations-log-and-logging.md) (operations log, debug logs)
- **Date:** 2026-09-26
- **Stage:** 1

## Context

The counter is to run 24/7, start automatically after a reboot and be easy to start with a debug window during development.

## Decision

- `TrafficCounter.Service` is based on the .NET Generic Host (`BackgroundService`) with `UseWindowsService()`. The same build runs as a console application or as a service.
- Configuration: `appsettings.json` (defaults, public) → `appsettings.{Environment}.json` → file from `TRAFFICCOUNTER_CONFIG` (private) → user secrets/environment variables (secrets) → command line. The options are validated at start-up with `ValidateOnStart`.
- The option `--preview` opens an OpenCV window with an overlay; it is only available in console mode.
- Logging with Serilog: console, rolling files and the Windows event log for errors.
- A watchdog reports when no frame has arrived for more than X seconds. The service then shuts down in a controlled way, and the recovery options of the Windows service restart it.

## Consequences

- No GUI of its own in the service; the analysis GUI (stage 4) is a separate application that only reads the database.
