# Windows Dashboard and CLI Design

**Date:** 2026-07-08  
**Status:** Approved  
**Platform:** Windows 11 23H2 or later

## Goal

Turn the existing Agent, IPC, and packaging foundation into a usable Windows application. The release provides a WinUI 3 dashboard for all supported usage queries and a command-line interface for the same read operations plus collection refresh.

## Scope

The App contains five NavigationView destinations: Summary, Projects, Models, Sessions, and Settings. The CLI contains `summary`, `projects`, `models`, `sessions`, and `refresh` commands. Tray behavior, export, localization, onboarding, and profile management remain outside this increment.

## Architecture

`AIUsageMonitor.App` becomes a packaged WinUI 3 application using the Windows App SDK. Views contain presentation-only XAML. ViewModels own loading state, period selection, paging, formatting, error state, and commands. A shared App query client maps typed IPC requests and responses to Core query models. The App never references Infrastructure or opens SQLite.

`AIUsageMonitor.Cli` uses the same command names and typed query models. It tries Agent IPC with a 500 ms timeout and falls back to `SqliteUsageQueryService` for read commands. `refresh` requires the Agent and returns exit code 5 with `agent_unavailable` when IPC cannot be reached.

## App Composition

- `App.xaml` initializes dependencies and opens `MainWindow`.
- `MainWindow` hosts NavigationView, a global Agent status indicator, data timestamp, and refresh action.
- Summary displays cards for input, output, cache-read, cache-write, and event totals.
- Projects and Models display paged tables ordered by total usage.
- Sessions displays session, project, totals, and last-event time in a paged table.
- Settings displays StartupTask state and an explicit enable/disable control.
- The default period is the current local calendar month converted to a UTC half-open range.

Each ViewModel exposes immutable display rows, `IsLoading`, `ErrorMessage`, `DataUpdatedAt`, paging state, and async commands. Only the active page loads automatically. Refresh requests collection refresh through IPC and then reloads the active page.

## Connection and Error Behavior

At startup the App follows the existing connection policy: 750 ms attempt, launch Agent once, then retry after 250/500/1000 ms. Connected state loads live data. Offline state retains the last successfully loaded data in memory, labels it as stale, disables mutation actions, and permits retry. Validation and protocol errors show a concise user-facing message without raw payloads or paths.

Cancellation from navigation or window close cancels pending page requests. Repeated refresh clicks share one in-flight operation. Empty results show an explicit empty state rather than an empty table.

## CLI Contract

Examples:

```text
ai-usage-status summary --from 2026-07-01 --to 2026-08-01
ai-usage-status projects --offset 0 --limit 50
ai-usage-status models --json
ai-usage-status sessions --limit 25
ai-usage-status refresh
```

Read commands print aligned text by default and stable camel-case JSON with `--json`. Dates are parsed as local calendar dates and converted to UTC boundaries. Invalid arguments return exit code 2. Agent-unavailable mutation returns 5. Unexpected failures return 1. Successful commands return 0.

## Testing

- ViewModel tests cover initial load, loading state, cancellation, empty results, stale/offline state, refresh serialization, paging, and error messages.
- Navigation tests verify page selection and active-page loading.
- IPC adapter tests cover command mapping and DTO conversion for all four query types and refresh.
- CLI tests cover parsing, text/JSON output, default date range, paging, IPC preference, database fallback, and exit codes.
- WinUI startup smoke testing verifies `MainWindow` creation on Windows.
- Existing Agent, IPC, Infrastructure, MSIX x64/ARM64, and recovery tests remain green.

## Acceptance Criteria

- Launching the packaged App opens a visible WinUI 3 dashboard.
- All five destinations work without direct database access from App.
- Summary and paged lists display Agent data and timestamps.
- Offline and empty states are explicit and non-crashing.
- StartupTask changes only after explicit user action.
- Every CLI command produces deterministic output and documented exit codes.
- Release x64 and ARM64 packages include the functional App, Agent, and CLI.
