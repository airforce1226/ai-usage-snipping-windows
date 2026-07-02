# Task 4 Report - concrete collection coordinator and watchers

## Status

DONE

## RED evidence

The focused `CollectionCoordinatorTests` run failed to compile with `CS0246` for the missing
`ProviderCollectionSource`, `CollectionCoordinator`, and watcher interfaces. This was the expected
failure before implementation.

## GREEN evidence

- Focused coordinator tests: 7 passed, 0 failed, 0 skipped.
- Full Infrastructure tests: 15 passed, 0 failed, 0 skipped.
- Tests use synchronization primitives and injectable fake watchers; no elapsed-time sleeps.

## Files

- `src/AIUsageMonitor.Core/Collection/ICollectionControl.cs`
- `src/AIUsageMonitor.Infrastructure/Collection/CollectionCoordinator.cs`
- `src/AIUsageMonitor.Infrastructure/Collection/ProviderFileWatcher.cs`
- `src/AIUsageMonitor.Infrastructure/Collection/SourceDiscoveryService.cs`
- `tests/AIUsageMonitor.Infrastructure.Tests/Collection/CollectionCoordinatorTests.cs`
- `.superpowers/sdd/task-4-report.md`

## Commit

`feat: coordinate background collection`

## Self-review

- The bounded channel has one reader and waits asynchronously when full.
- Canonical paths are coalesced case-insensitively while queued or in flight.
- Resume drains reconciliation before starting provider watchers; pause disposes them idempotently.
- Per-file failures update provider health and do not terminate the queue worker.
- Drain observes queued and in-flight work and does not cancel processing on caller timeout.
- Existing `IncrementalFileReader` remains responsible for checkpoint and stable-key semantics.

## Concerns

- `FileSystemWatcher` callbacks are best-effort by platform design; watcher buffer errors trigger a
  full provider reconciliation.
- Provider health records the latest processing exception and last successful processing time; it
  intentionally does not persist health across process restarts.
