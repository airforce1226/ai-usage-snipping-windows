# Per-User Agent Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split AI Usage Monitor into a per-user background Agent and a WinUI dashboard that communicate securely over local named pipes.

**Architecture:** `AIUsageMonitor.Ipc` owns versioned contracts, frame encoding, pipe naming, and client/server transport. `AIUsageMonitor.Agent` hosts the existing collection and SQLite services in a single per-user background process; App and CLI become IPC clients with explicit offline/read-only fallback behavior.

**Tech Stack:** C# 12, .NET 8, Windows named pipes, System.Text.Json, WindowsIdentity/SID, Microsoft.Extensions.Hosting, Microsoft.Data.Sqlite, xUnit.

## Global Constraints

- Windows 11 23H2 or later; x64 and ARM64.
- Agent runs with ordinary interactive-user permissions and never elevates.
- Pipe ACL authorizes only the current user SID and LocalSystem.
- Protocol version is integer `1`; maximum JSON payload is exactly `1,048,576` bytes.
- Frame format is four-byte little-endian payload length followed by UTF-8 JSON.
- App never starts watchers or opens a write-capable SQLite connection.
- Agent owns all collection and database mutation.
- CLI mutation commands fail with exit code `5` when Agent is unavailable.
- No prompt/response content, credentials, raw SID, or raw IPC payloads are logged.
- Existing parser, incremental reader, and SQLite behavior must remain unchanged.

---

### Task 1: IPC contracts and bounded framing

**Files:**
- Create: `src/AIUsageMonitor.Ipc/AIUsageMonitor.Ipc.csproj`
- Create: `src/AIUsageMonitor.Ipc/Contracts/IpcRequest.cs`
- Create: `src/AIUsageMonitor.Ipc/Contracts/IpcResponse.cs`
- Create: `src/AIUsageMonitor.Ipc/Contracts/IpcError.cs`
- Create: `src/AIUsageMonitor.Ipc/Transport/IpcFrameCodec.cs`
- Create: `tests/AIUsageMonitor.Ipc.Tests/AIUsageMonitor.Ipc.Tests.csproj`
- Create: `tests/AIUsageMonitor.Ipc.Tests/Transport/IpcFrameCodecTests.cs`
- Modify: `AIUsageMonitor.sln`

**Interfaces:**
- Produces: `IpcRequest(int ProtocolVersion, string RequestId, string Type, JsonElement Payload)`.
- Produces: `IpcResponse(int ProtocolVersion, string RequestId, string Type, JsonElement? Payload, IpcError? Error)`.
- Produces: `IpcFrameCodec.WriteAsync<T>(Stream, T, CancellationToken)` and `ReadAsync<T>(Stream, CancellationToken)`.

- [ ] **Step 1: Create projects and references**

```powershell
dotnet new classlib -n AIUsageMonitor.Ipc -o src/AIUsageMonitor.Ipc -f net8.0
dotnet new xunit -n AIUsageMonitor.Ipc.Tests -o tests/AIUsageMonitor.Ipc.Tests -f net8.0 --no-restore
dotnet sln AIUsageMonitor.sln add src/AIUsageMonitor.Ipc/AIUsageMonitor.Ipc.csproj tests/AIUsageMonitor.Ipc.Tests/AIUsageMonitor.Ipc.Tests.csproj
dotnet add src/AIUsageMonitor.Ipc/AIUsageMonitor.Ipc.csproj reference src/AIUsageMonitor.Core/AIUsageMonitor.Core.csproj
dotnet add tests/AIUsageMonitor.Ipc.Tests/AIUsageMonitor.Ipc.Tests.csproj reference src/AIUsageMonitor.Ipc/AIUsageMonitor.Ipc.csproj
```

- [ ] **Step 2: Write failing framing tests**

Tests must round-trip a request through `MemoryStream`, reject zero length, reject `1_048_577`, reject truncated payloads, and reject malformed UTF-8. The oversized test writes `BitConverter.GetBytes(1_048_577)` followed by no payload and expects `InvalidDataException` containing `maximum`.

- [ ] **Step 3: Run RED**

Run: `dotnet test tests/AIUsageMonitor.Ipc.Tests -c Release --no-restore`

Expected: compilation fails because `IpcFrameCodec` and contract records do not exist.

- [ ] **Step 4: Implement contracts and codec**

Use `BinaryPrimitives.WriteInt32LittleEndian`, `ReadExactlyAsync`, strict `UTF8Encoding(false, true)`, and `JsonSerializer`. Validate length before allocating. `WriteAsync` serializes to UTF-8 bytes, rejects payloads outside `1..1_048_576`, writes prefix then payload, and flushes. `ReadAsync` reads exactly four bytes, validates, rents from `ArrayPool<byte>`, reads exactly the declared bytes, decodes strict UTF-8, deserializes, and returns the rented buffer in `finally`.

- [ ] **Step 5: Run GREEN and commit**

```powershell
dotnet test tests/AIUsageMonitor.Ipc.Tests -c Release --no-restore
git add AIUsageMonitor.sln src/AIUsageMonitor.Ipc tests/AIUsageMonitor.Ipc.Tests
git commit -m "feat: define bounded IPC protocol"
```

Expected: all IPC codec tests pass.

### Task 2: User-scoped pipe and mutex identity

**Files:**
- Create: `src/AIUsageMonitor.Ipc/Security/UserEndpointIdentity.cs`
- Create: `src/AIUsageMonitor.Ipc/Security/ICurrentUserIdentity.cs`
- Create: `src/AIUsageMonitor.Ipc/Security/WindowsCurrentUserIdentity.cs`
- Test: `tests/AIUsageMonitor.Ipc.Tests/Security/UserEndpointIdentityTests.cs`

**Interfaces:**
- Produces: `UserEndpointIdentity.Create(string sid, int protocolMajor)` returning `PipeName` and `MutexName`.
- Produces: `ICurrentUserIdentity.GetSid()`.

- [ ] **Step 1: Write failing deterministic identity tests**

Use SID strings `S-1-5-21-100-200-300-1001` and `S-1-5-21-100-200-300-1002`. Assert equal SID/version gives equal names, different SID gives different names, and the raw SID is absent. Assert pipe matches `^AIUsageMonitor\.Agent\.v1\.[0-9a-f]{16}$`.

- [ ] **Step 2: Run RED**

Run: `dotnet test tests/AIUsageMonitor.Ipc.Tests --filter FullyQualifiedName~UserEndpointIdentityTests -c Release --no-restore`

Expected: compilation fails for missing identity types.

- [ ] **Step 3: Implement identity derivation**

Hash the UTF-8 SID with SHA-256, lowercase the first eight bytes as 16 hexadecimal characters, and build `AIUsageMonitor.Agent.v{major}.{hash}` plus `Local\AIUsageMonitor.Agent.v{major}.{hash}`. `WindowsCurrentUserIdentity` reads `WindowsIdentity.GetCurrent().User.Value` and throws `InvalidOperationException` if unavailable.

- [ ] **Step 4: Run GREEN and commit**

```powershell
dotnet test tests/AIUsageMonitor.Ipc.Tests -c Release --no-restore
git add src/AIUsageMonitor.Ipc/Security tests/AIUsageMonitor.Ipc.Tests/Security
git commit -m "feat: scope agent endpoints to Windows user"
```

### Task 3: Named-pipe request server and client

**Files:**
- Create: `src/AIUsageMonitor.Ipc/Transport/IIpcRequestHandler.cs`
- Create: `src/AIUsageMonitor.Ipc/Transport/NamedPipeAgentServer.cs`
- Create: `src/AIUsageMonitor.Ipc/Transport/NamedPipeAgentClient.cs`
- Test: `tests/AIUsageMonitor.Ipc.Tests/Transport/NamedPipeRoundTripTests.cs`

**Interfaces:**
- Consumes: `IpcFrameCodec`, `UserEndpointIdentity`, `IIpcRequestHandler.HandleAsync(IpcRequest, CancellationToken)`.
- Produces: `NamedPipeAgentServer.RunAsync(CancellationToken)` and `NamedPipeAgentClient.SendAsync(IpcRequest, TimeSpan, CancellationToken)`.

- [ ] **Step 1: Write failing round-trip tests**

Start a server on a unique test pipe, send `agent.health.get`, and assert response request ID/type. Add tests for `protocolVersion=2` returning `unsupported_protocol`, unknown type returning `unknown_command`, client timeout, and five concurrent clients receiving their own request IDs.

- [ ] **Step 2: Run RED**

Run: `dotnet test tests/AIUsageMonitor.Ipc.Tests --filter FullyQualifiedName~NamedPipeRoundTripTests -c Release --no-restore`

Expected: compilation fails for missing server/client.

- [ ] **Step 3: Implement server and client**

Use `NamedPipeServerStream` in byte mode, asynchronous mode, one server instance per accept loop, and `PipeOptions.CurrentUserOnly`. Bound concurrent handlers with `SemaphoreSlim(8, 8)`. Validate protocol and command before dispatch. A client connects with the provided timeout, writes one request, reads one response, and disposes its pipe.

- [ ] **Step 4: Run GREEN and commit**

```powershell
dotnet test tests/AIUsageMonitor.Ipc.Tests -c Release --no-restore
git add src/AIUsageMonitor.Ipc/Transport tests/AIUsageMonitor.Ipc.Tests/Transport
git commit -m "feat: exchange requests over named pipes"
```

### Task 4: Background Agent host and lifecycle

**Files:**
- Create: `src/AIUsageMonitor.Agent/AIUsageMonitor.Agent.csproj`
- Create: `src/AIUsageMonitor.Agent/Program.cs`
- Create: `src/AIUsageMonitor.Agent/AgentRuntime.cs`
- Create: `src/AIUsageMonitor.Agent/AgentRequestHandler.cs`
- Create: `src/AIUsageMonitor.Agent/AgentState.cs`
- Create: `src/AIUsageMonitor.Agent/ICollectionControl.cs`
- Create: `src/AIUsageMonitor.Agent/SingleInstanceGuard.cs`
- Create: `tests/AIUsageMonitor.Agent.Tests/AIUsageMonitor.Agent.Tests.csproj`
- Create: `tests/AIUsageMonitor.Agent.Tests/AgentRequestHandlerTests.cs`
- Create: `tests/AIUsageMonitor.Agent.Tests/AgentRuntimeTests.cs`
- Modify: `AIUsageMonitor.sln`

**Interfaces:**
- Produces: health, pause, resume, refresh, and shutdown handlers.
- Produces: state values `Starting`, `Running`, `Paused`, `Degraded`, `Stopping`.
- Consumes: existing `IncrementalFileReader` and `SqliteUsageEventStore` through `ICollectionControl.RefreshAsync`, `PauseAsync`, `ResumeAsync`, and `DrainAsync`.

- [ ] **Step 1: Create Agent projects**

Create a .NET 8 console Agent and xUnit test project, add them to the solution, and reference Core, Infrastructure, and IPC from Agent; reference Agent from tests.

- [ ] **Step 2: Write failing lifecycle tests**

Use fake collection control and fake shutdown signal. Assert health payload state/timestamps, pause is idempotent, resume performs refresh before watchers start, shutdown transitions to `Stopping`, rejects mutation, drains with a 10-second fake deadline, and returns exit code `12` on deadline expiration.

- [ ] **Step 3: Run RED**

Run: `dotnet test tests/AIUsageMonitor.Agent.Tests -c Release --no-restore`

Expected: compilation fails for missing Agent runtime types.

- [ ] **Step 4: Implement minimal Agent host**

Use `Host.CreateApplicationBuilder`, register one `AgentRuntime`, pipe server, request handler, existing database factory/migrator/store, and collection interfaces. `SingleInstanceGuard` creates the SID-derived named mutex and returns false when already held. `Program` exits `10` for duplicate instance, `12` for drain timeout, otherwise `0`.

- [ ] **Step 5: Run GREEN and commit**

```powershell
dotnet test tests/AIUsageMonitor.Agent.Tests -c Release --no-restore
dotnet test tests/AIUsageMonitor.Ipc.Tests -c Release --no-build --no-restore
git add AIUsageMonitor.sln src/AIUsageMonitor.Agent tests/AIUsageMonitor.Agent.Tests
git commit -m "feat: host collection in per-user agent"
```

### Task 5: App and CLI connection policy

**Files:**
- Create: `src/AIUsageMonitor.App/Services/AgentConnectionService.cs`
- Create: `src/AIUsageMonitor.App/Services/AgentProcessLauncher.cs`
- Create: `src/AIUsageMonitor.App/ViewModels/AgentConnectionState.cs`
- Create: `src/AIUsageMonitor.Cli/AgentOrDatabaseQueryClient.cs`
- Test: `tests/AIUsageMonitor.App.Tests/Services/AgentConnectionServiceTests.cs`
- Test: `tests/AIUsageMonitor.Cli.Tests/AgentOrDatabaseQueryClientTests.cs`
- Modify: `src/AIUsageMonitor.App/AIUsageMonitor.App.csproj`
- Modify: `src/AIUsageMonitor.Cli/AIUsageMonitor.Cli.csproj`

**Interfaces:**
- Produces: App connect sequence `750 ms -> launch once -> 250/500/1000 ms retries -> Offline`.
- Produces: CLI `QueryAsync` using 500 ms IPC then read-only SQLite fallback.

- [ ] **Step 1: Write failing App retry-policy tests**

Use fake clock/client/launcher. Assert exact timeout/delay sequence, one launch, connected state on retry, offline state after exhaustion, and no more than three automatic restarts in ten minutes.

- [ ] **Step 2: Write failing CLI fallback tests**

Assert IPC result wins when available, read-only database query runs after IPC timeout, and mutation returns exit code `5` without invoking database mutation.

- [ ] **Step 3: Run RED**

Run App and CLI test projects filtered to the new test classes. Expected: compilation fails for missing connection services.

- [ ] **Step 4: Implement connection services**

Inject clock, client factory, process launcher, and read-only query interface. Do not use `Task.Delay` directly in policy logic. Remove App's Infrastructure project reference after all App database calls route through Agent contracts; keep CLI's Infrastructure reference only for read-only fallback.

- [ ] **Step 5: Run GREEN and commit**

```powershell
dotnet test tests/AIUsageMonitor.App.Tests -c Release --no-restore
dotnet test tests/AIUsageMonitor.Cli.Tests -c Release --no-restore
git add src/AIUsageMonitor.App src/AIUsageMonitor.Cli tests/AIUsageMonitor.App.Tests tests/AIUsageMonitor.Cli.Tests
git commit -m "feat: connect app and CLI to agent"
```

### Task 6: StartupTask packaging and end-to-end verification

**Files:**
- Create: `packaging/Package.appxmanifest`
- Create: `tests/AIUsageMonitor.Agent.Tests/SingleInstanceIntegrationTests.cs`
- Create: `tests/AIUsageMonitor.Agent.Tests/AgentRestartRecoveryTests.cs`
- Create: `docs/qa/agent-architecture-checklist.md`

**Interfaces:**
- Produces: MSIX `StartupTask` launching `AIUsageMonitor.Agent.exe` after logon.

- [ ] **Step 1: Add failing manifest validation test**

Parse `Package.appxmanifest` and assert an enabled `desktop6:StartupTask` references `AIUsageMonitor.Agent.exe`, App remains the visible application, and minimum OS build is `22621`.

- [ ] **Step 2: Implement manifest and startup setting bridge**

Add startup task ID `AIUsageMonitorAgentStartup`. App settings use `StartupTask.GetAsync`, `RequestEnableAsync`, and `Disable` and display the returned state without elevation.

- [ ] **Step 3: Add integration tests**

Run two Agent processes against a temporary profile and assert the second exits `10`. Kill the first after a committed checkpoint, append one event, restart, refresh, and assert exactly one new event and zero duplicates.

- [ ] **Step 4: Run complete verification**

```powershell
dotnet build AIUsageMonitor.sln -c Release --no-restore
dotnet test AIUsageMonitor.sln -c Release --no-build --no-restore
git diff --check
git status --short
```

Expected: build has zero warnings/errors, every discovered test passes, diff check is empty, and only intended packaging/docs changes remain before commit.

- [ ] **Step 5: Commit**

```powershell
git add packaging docs/qa tests/AIUsageMonitor.Agent.Tests src/AIUsageMonitor.App
git commit -m "chore: start per-user agent after logon"
```
