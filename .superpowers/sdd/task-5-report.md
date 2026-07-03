# Task 5 Report — Query contracts and read APIs

## Result

Implemented protocol-stable usage query contracts and a shared SQLite read-only query service for summary, project, model, and session views.

## RED evidence

- IPC contract tests failed to compile because all usage request/response contracts, command names, validation codes, and JSON options were absent.
- Infrastructure query tests failed to compile because the Core query surface and `SqliteUsageQueryService` were absent.

## GREEN evidence

- Focused IPC contract tests: 13 passed, 0 failed.
- Focused SQLite query tests: 4 passed, 0 failed.
- Full Core tests: 4 passed, 0 failed.
- Full IPC tests: 33 passed, 0 failed.
- Full Infrastructure tests: 29 passed, 0 failed.

## Files

- `src/AIUsageMonitor.Core/Queries/IUsageQueryService.cs`
- `src/AIUsageMonitor.Core/Queries/UsageQueryModels.cs`
- `src/AIUsageMonitor.Ipc/Contracts/UsageQueryContracts.cs`
- `src/AIUsageMonitor.Infrastructure/Queries/SqliteUsageQueryService.cs`
- `tests/AIUsageMonitor.Ipc.Tests/Contracts/UsageQueryContractTests.cs`
- `tests/AIUsageMonitor.Infrastructure.Tests/Queries/SqliteUsageQueryServiceTests.cs`

## Self-review

- Query service receives only a trusted composition-time database path; IPC payloads contain no path or SQL.
- SQL values are parameterized; fixed grouping columns are internal constants selected by typed methods.
- Query connection uses `Mode=ReadOnly;Cache=Shared` and cannot create or mutate the database.
- All range predicates are half-open and aggregation occurs in SQLite.
- Stable secondary ordering and page metadata are included.
- Cancellation tokens reach connection, command, reader, and scalar async calls.
- No parser, migration, write-store, cost, or pricing behavior changed.

## Commit

`feat: define agent usage queries`

## Concerns

- `DatabaseUpdatedAtUtc` is derived from the latest stored event occurrence or checkpoint last-write timestamp because the existing schema has no separate database mutation timestamp.

## Review fix — 2026-07-03

- RED: Core and IPC tests failed because `non_utc_timestamp` did not exist; session tests returned three rows for one session and counted six project/session groups instead of four unique sessions.
- GREEN: session aggregation now groups only by session ID, selects the project from the latest event with descending stable-key tie-break, and retains deterministic session ordering and paging metadata.
- UTC validation rejects non-zero offsets in both Core and IPC with `non_utc_timestamp`; JSON round-trip tests execute under `ar-SA` current and UI cultures.
- Focused: Core query 2/2, IPC contract 14/14, Infrastructure query 6/6 passed.
- Full: Core 6/6, IPC 34/34, Infrastructure 31/31 passed.
- Fix commit: `fix: stabilize usage query contracts`.
