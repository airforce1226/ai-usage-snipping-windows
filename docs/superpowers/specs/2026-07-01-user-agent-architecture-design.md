# AI Usage Monitor User Agent Architecture

**Date:** 2026-07-01  
**Status:** Approved design change  
**Platform:** Windows 11 23H2 or later

## 1. Decision

AI Usage Monitor uses a per-user background Agent rather than a machine-level Windows Service. The Agent starts after user logon, runs without a visible window, and continues collecting Claude Code and Codex usage when the WinUI dashboard is closed.

A machine-level Windows Service is explicitly excluded. Service accounts do not naturally share the interactive user's profile, `%USERPROFILE%\.claude`, `%USERPROFILE%\.codex`, HUD configuration, language, or MSIX user context. Avoiding a privileged service also removes administrator installation and cross-user data-boundary problems.

## 2. Process responsibilities

### `AIUsageMonitor.Agent.exe`

- Starts through an MSIX `StartupTask` after user logon.
- Owns file discovery, `FileSystemWatcher`, reconciliation, parsing, pricing, SQLite writes, aggregate queries, provider health, and collection pause/resume state.
- Owns the only read-write SQLite connection pool.
- Runs as one instance per Windows user session.
- Has no window, tray icon, toast UI, or direct user prompts.
- Exposes the local IPC endpoint.
- Flushes collection work and checkpoints before planned shutdown.

### `AIUsageMonitor.App.exe`

- Owns WinUI pages, onboarding, settings UX, tray icon, localization, accessibility, exports, and update UX.
- Connects to the Agent through IPC and never starts its own watcher or write-capable database store.
- Starts the Agent when it is missing, retries connection, and presents explicit offline/degraded states.
- May exit while collection continues.
- Keeps the tray process alive only when the user enables `Keep tray UI running`.

### `ai-usage-status.exe`

- Uses Agent IPC when the Agent is available.
- Falls back to a read-only SQLite connection when the Agent is unavailable.
- Never starts watchers or writes collection data.

## 3. Startup and lifetime

The first App launch starts the non-elevated Agent, waits up to five seconds for IPC readiness, and then performs onboarding. Automatic collection is an explicit user opt-in: only after the user enables it does the MSIX `StartupTask` start the Agent at subsequent logons. The StartupTask is not enabled by default.

The Agent uses a named mutex containing the current Windows user SID. A second Agent instance detects the mutex and exits with code `10`. The mutex name and pipe name include an application protocol major version so incompatible side-by-side development builds do not connect accidentally.

Default App close behavior exits the App. The Agent remains running. When `Keep tray UI running` is enabled, closing the window hides it and keeps the App tray icon active. The tray icon is never hosted by the Agent.

Pausing collection stops watchers and scheduled reconciliation but leaves IPC, settings, health, and read queries available. Resuming performs an immediate reconciliation before restarting watchers.

## 4. IPC contract

Use local named pipes. The server pipe name is `AIUsageMonitor.Agent.v1.{userSidHash}`. The raw SID is not exposed in logs; the pipe suffix is the first 16 lowercase hexadecimal characters of SHA-256 over the SID string.

Both server and clients create their pipe streams with `PipeOptions.CurrentUserOnly`. This restricts IPC to processes running as the same current interactive Windows user and also protects clients from connecting to a pipe name pre-created by another user. The design does not claim a separate, categorical remote-client rejection guarantee beyond the behavior supplied by `CurrentUserOnly`; any stronger guarantee requires an implemented and tested transport control. The server accepts multiple sequential clients and processes at most eight concurrent requests.

Frames use a four-byte little-endian payload length followed by UTF-8 JSON. Maximum JSON payload length is 1,048,576 bytes. Both client and server reject zero-length, oversized, truncated, invalid UTF-8, and malformed JSON frames.

Every request contains:

```json
{
  "protocolVersion": 1,
  "requestId": "01J2ABCDEF0123456789ABCDEFG",
  "type": "usage.summary.get",
  "payload": {}
}
```

Every response echoes `protocolVersion`, `requestId`, and `type`, and contains either `payload` or an error object with stable `code` and localized-independent `message`.

Version 1 commands are:

- `agent.health.get`
- `agent.shutdown`
- `collection.pause`
- `collection.resume`
- `collection.refresh`
- `usage.summary.get`
- `usage.projects.get`
- `usage.models.get`
- `usage.sessions.get`
- `settings.get`
- `settings.update`
- `profile.list`
- `profile.select`

Unknown commands return `unknown_command`. Unsupported protocol versions return `unsupported_protocol`. Cancellation closes the client request; long-running server work observes a linked cancellation token.

## 5. Connection and fallback behavior

App connection sequence:

1. Connect to the current user's pipe with a 750 ms timeout.
2. If unavailable, start the packaged Agent process once.
3. Retry with delays of 250 ms, 500 ms, and 1,000 ms.
4. If still unavailable, enter read-only offline mode and show the last database update time.

The App may automatically restart a crashed Agent at most three times in ten minutes. Further failures require explicit user action and expose redacted diagnostics.

App, Agent, and CLI all run non-elevated. An elevated/non-elevated token mismatch is outside the supported IPC topology; when it prevents a connection, App enters its offline/degraded experience and CLI follows its read-only fallback rather than elevating either process.

The CLI attempts one 500 ms IPC connection. If unavailable, it opens the active profile database with `Mode=ReadOnly;Cache=Shared`. Commands that require mutation fail with exit code `5` and error `agent_unavailable`.

## 6. Agent status model

The externally visible states are:

- `Starting`: process exists but initialization or migration is incomplete.
- `Running`: collection and reconciliation are active.
- `Paused`: collection is intentionally stopped.
- `Degraded`: one provider or background component failed while queries remain available.
- `Stopping`: new mutation requests are rejected while work drains.
- `Offline`: the App cannot connect to an Agent.

Responses include `startedAtUtc`, `lastSuccessfulCollectionAtUtc`, `databaseUpdatedAtUtc`, provider states, queue depth, and the last redacted error code. The UI always shows the data timestamp when state is not `Running`.

## 7. Graceful shutdown and updates

Planned Agent shutdown follows this order:

1. Transition to `Stopping` and reject new mutation requests.
2. Stop accepting new file-change work.
3. Dispose file watchers and scheduled reconciliation.
4. Drain the bounded collection queue with a 10-second deadline.
5. Commit the last event/checkpoint transaction.
6. Close SQLite connections and clear pools.
7. Stop the pipe listener and release the mutex.
8. Exit with code `0`.

If the deadline expires, the Agent cancels remaining reads, preserves the last committed checkpoint, and exits with code `12`. Reconciliation on the next start recovers unprocessed bytes without duplicate events.

Before an update, the App sends `agent.shutdown`, waits up to 15 seconds, and only then launches package update. Forced process termination is a last resort and is surfaced in diagnostics.

## 8. Security and privacy

- The Agent runs with ordinary interactive-user permissions and never elevates.
- Server and client named-pipe streams use `PipeOptions.CurrentUserOnly`; LocalSystem is not separately authorized.
- App, Agent, and CLI run non-elevated. Elevation mismatch is handled as Agent unavailability, never by privilege escalation.
- App, Agent, and CLI validate protocol version, command type, payload size, and JSON shape.
- Requests cannot contain arbitrary SQL or filesystem paths except paths validated by the settings/profile layer.
- Logs never include raw SID, prompt/response content, credentials, pipe payloads, or unredacted project paths.
- The Agent reads provider source logs and writes only AI Usage Monitor-owned files.
- Database mutation is unavailable through CLI fallback.

## 9. Error handling

One provider failure does not stop the other provider or IPC queries. Agent background tasks report structured health failures rather than terminating the process. Unhandled background exceptions transition the affected component to `Degraded` and schedule bounded recovery.

Database migration failure prevents collection and mutation IPC but allows a health response containing `database_migration_failed`. Pipe client failures are isolated per connection. Invalid or oversized frames close only that client connection.

App retains the last valid view model and labels it with the last update timestamp when IPC fails. It never displays cached values as live.

## 10. Test requirements

- A second Agent instance exits while the first remains healthy.
- The mutex and pipe names differ between simulated user SIDs.
- Current-user IPC succeeds, a different-user identity cannot use the endpoint in an integration environment, and client-side `CurrentUserOnly` prevents cross-user pipe-name squatting.
- Valid, malformed, oversized, truncated, unknown-command, and unsupported-version frames behave as specified.
- Request cancellation and client disconnect do not leak server tasks.
- Closing App leaves Agent collection running.
- Agent restart resumes from persisted checkpoints and inserts zero duplicates.
- Graceful shutdown drains queued events and closes the database.
- Deadline shutdown retains the last committed checkpoint and recovers on restart.
- App connection retry and three-restarts-per-ten-minutes policy are deterministic under a fake clock.
- CLI uses IPC when available and read-only fallback when unavailable.
- StartupTask opt-in enable/disable, package update, and native SQLite behavior pass for explicit `win-x64` and `win-arm64` publish/bundle outputs on clean Windows 11 VMs.

## 11. Repository changes

Add these projects:

- `src/AIUsageMonitor.Agent/AIUsageMonitor.Agent.csproj`
- `src/AIUsageMonitor.Ipc/AIUsageMonitor.Ipc.csproj`
- `tests/AIUsageMonitor.Agent.Tests/AIUsageMonitor.Agent.Tests.csproj`
- `tests/AIUsageMonitor.Ipc.Tests/AIUsageMonitor.Ipc.Tests.csproj`
- a Windows Application Packaging Project that produces the MSIX bundle and declares the opt-in `StartupTask`

`AIUsageMonitor.Ipc` contains contracts, framing, pipe-name derivation, client, and server transport. It depends only on `AIUsageMonitor.Core`. Agent depends on Core, Infrastructure, and IPC. App depends on Core, HUD, and IPC; it does not reference Infrastructure after the transition. CLI depends on Core, Infrastructure for read-only fallback, and IPC.

The existing parsing, incremental reading, and SQLite implementation remain valid and move under Agent composition without behavior changes.
