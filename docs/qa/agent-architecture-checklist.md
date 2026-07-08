# Per-User Agent Architecture QA Checklist

## Automated on Windows x64

- [x] Named pipe framing, protocol validation, cancellation, disconnect, and concurrency tests pass.
- [x] Current-user pipe options are enabled on both client and server.
- [x] A second in-process Agent guard cannot acquire the same user-scoped mutex.
- [x] App connection retries and the three-restarts-per-ten-minutes limit pass under deterministic test doubles.
- [x] CLI prefers Agent queries, falls back to read-only SQLite, and returns exit code 5 for unavailable mutations.
- [x] StartupTask remains disabled until explicit opt-in and exposes all returned states.
- [x] Restart recovery resumes from the persisted checkpoint and inserts no duplicate events.
- [x] Release x64 and ARM64 MSIX packages and an x64/ARM64 bundle build successfully.
- [x] Both architecture packages contain App, Agent, CLI, and native `e_sqlite3.dll` files for Agent and CLI.

## Controlled-environment verification still required

- [ ] Install the unsigned/development-signed x64 package on a clean Windows 11 23H2-or-later x64 VM.
- [ ] Install the unsigned/development-signed ARM64 package on a clean Windows 11 23H2-or-later ARM64 VM.
- [ ] Confirm StartupTask is initially disabled, then opt in/out through App and verify logoff/logon behavior.
- [ ] Verify same-user IPC succeeds and a separate non-administrator Windows user cannot connect.
- [ ] Pre-create the predictable pipe name from the second user and confirm client-side `CurrentUserOnly` rejects it.
- [ ] Collect and query fixtures through Agent, App, and CLI on both architectures.
- [ ] Confirm App exit leaves Agent collection active and planned shutdown drains and closes SQLite.
- [ ] Confirm forced interruption after a committed checkpoint recovers exactly the unprocessed events.

Do not claim the controlled-environment items as passed until they have been executed on the stated clean VMs.
