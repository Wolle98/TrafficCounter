# ADR-0014: Technology of the analysis GUI

- **Status:** accepted, extended by [ADR-0037](0037-multilingual-analysis-app.md) (multilingual) and [ADR-0051](0051-day-notes-and-holidays.md) (notes on days; the app is no longer read-only)
- **Date:** 2026-09-26, decided 2026-10-06
- **Stage:** 4

## Context

Convenient analysis with charts, filters and reports (PDF/Excel). The GUI only reads from the database.

## Candidates

| Option | Pro | Contra |
|---|---|---|
| WPF + LiveCharts2/ScottPlot | mature, Windows-native | Windows only |
| Avalonia + LiveCharts2/ScottPlot | cross-platform, XAML | smaller ecosystem |
| Blazor (Server/WASM) + ApexCharts/MudBlazor | reachable from anywhere via browser, also on the phone | web server operation, authentication needed |

Reports: QuestPDF (check the community licence) or ClosedXML for Excel.

## Decision

We choose **Blazor Server** (`TrafficCounter.App`), because:

- **Data location:** the data is on the NAS (PostgreSQL, [ADR-0030](0030-postgresql-and-synchronisation.md)); the counting machine runs without a screen.
- **Access without installation:** a web app on the notebook or the PC is reachable from any device in the home network, including the phone.
- **Code reuse:** Blazor Server uses the data access from `TrafficCounter.Data` directly. There is no separate API and no second client.

Further decisions:

- **Charts:** as SVG directly in Razor components, without a chart library, without JavaScript and without a CDN. This works without internet and needs no licence check.
- **Database:** the same settings as in the service (`Storage:Provider`, connection string `TrafficCounter`, password from `TRAFFICCOUNTER_PG_PASSWORD`).
- **Read-only:** the app only reads. Only what the service recorded is counted; hand counts are left out ([ADR-0027](0027-hand-counted-passages.md)).
- **Gaps:** rates per hour refer to the minutes actually measured ([ADR-0024](0024-operations-log-and-logging.md)). Gaps are marked instead of concealed.
- **Reachability:** by default the app only listens on `localhost`. Only with a deliberately set address is it reachable in the home network. There is no login for now. The app must not be exposed to the internet like this.
- **Reports** (PDF via QuestPDF, Excel via ClosedXML) come later. Initially there is a CSV export.

## Implementation note (2026-10-07)

Excel export is implemented with ClosedXML. For PDF there is a print view that the browser saves as PDF; QuestPDF was not needed so far.
