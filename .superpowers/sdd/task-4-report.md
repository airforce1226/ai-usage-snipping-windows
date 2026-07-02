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

## Concurrency hardening review

Review findings were reproduced with deterministic gates before correction. The coordinator now
uses source-plus-canonical-path work keys, preserves a dirty rerun while work is in flight, bounds
unique admitted work, and atomically transitions work to idle so `DrainAsync` cannot observe an
intermediate completion. An async lifecycle gate serializes pause/resume and disposes partial watcher
sets. Resume creates disabled watchers, reconciles, starts them, and performs a final reconciliation.

Discovery failures are isolated into provider health. The concrete watcher uses a bounded event pump,
signals reconciliation on overflow/error/callback failure, and awaits the pump during disposal so
callback exceptions are observed. The previously mislabeled provider fixture was corrected.

- Focused coordinator/watcher tests: 15 passed, 0 failed, 0 skipped.
- Full Infrastructure tests: 23 passed, 0 failed, 0 skipped.
- Review-fix commit: `fix: harden collection coordinator concurrency`.

## Drain and disposal linearization follow-up

Two deterministic regressions were added. Before the fix, drain completed while a second accepted
enqueue was waiting for the bounded ingress slot, and a resume already waiting on the lifecycle gate
succeeded after disposal. Ingress entrants now participate in the same idle completion state as queued
and in-flight work. Disposal marks the coordinator disposed and tears down watchers and the worker
while holding the lifecycle gate; resume rechecks disposal after acquiring that gate.

- Targeted race tests: 2 passed, 0 failed, 0 skipped.
- Focused coordinator/watcher tests: 17 passed, 0 failed, 0 skipped.
- Full Infrastructure tests: 25 passed, 0 failed, 0 skipped.
- Follow-up commit: `fix: linearize collection drain and disposal`.
