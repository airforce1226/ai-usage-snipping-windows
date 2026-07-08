# Windows Dashboard and CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a visible WinUI 3 usage dashboard and a deterministic `ai-usage-status` CLI over the existing Agent query protocol.

**Architecture:** A typed `AgentUsageQueryClient` in the IPC project implements `IUsageQueryService` and is shared by the App and CLI. The CLI adds parsing, rendering, Agent-first/database-fallback composition, while the App adds testable ViewModels and thin WinUI 3 XAML views; packaging remains the final integration boundary.

**Tech Stack:** .NET 8, C# 12, Windows App SDK/WinUI 3, named-pipe JSON IPC, Microsoft.Data.Sqlite, xUnit, MSIX Packaging Project

## Global Constraints

- Support Windows 11 23H2 or later; retain `10.0.22621.0` as the target minimum.
- App code references Core and IPC only and must never reference Infrastructure or open SQLite.
- Default range is the current local calendar month converted to a UTC half-open interval.
- Page size is 50 by default and must remain in the Core-supported range 1 through 200.
- Agent IPC query timeout is 500 ms for CLI reads; App startup retains 750 ms plus 250/500/1000 ms retries.
- CLI exit codes are 0 success, 1 unexpected failure, 2 invalid arguments, and 5 unavailable Agent for mutation.
- Keep existing Agent, IPC, Infrastructure, recovery, and x64/ARM64 packaging tests green.

---

## File Structure

- `src/AIUsageMonitor.Ipc/Client/AgentUsageQueryClient.cs`: typed query and refresh adapter over `NamedPipeAgentClient`.
- `src/AIUsageMonitor.Cli/CliApplication.cs`: command dispatch and exit-code boundary.
- `src/AIUsageMonitor.Cli/CliOptions.cs`: argument parsing and local-date conversion.
- `src/AIUsageMonitor.Cli/UsageOutputWriter.cs`: stable text and camel-case JSON output.
- `src/AIUsageMonitor.Cli/CliComposition.cs`: default pipe, database path, and fallback wiring.
- `src/AIUsageMonitor.App/ViewModels/*`: observable, UI-independent state and commands.
- `src/AIUsageMonitor.App/Services/AppComposition.cs`: App service construction without Infrastructure.
- `src/AIUsageMonitor.App/Views/*`: one thin XAML page per destination.
- `src/AIUsageMonitor.App/App.xaml*`, `MainWindow.xaml*`: WinUI lifetime, shell, navigation, and global status.
- `tests/AIUsageMonitor.Ipc.Tests/Client/*`, `tests/AIUsageMonitor.Cli.Tests/*`, `tests/AIUsageMonitor.App.Tests/ViewModels/*`: behavioral tests.

### Task 1: Typed Agent Query Client

**Files:**
- Create: `src/AIUsageMonitor.Ipc/Client/AgentUsageQueryClient.cs`
- Modify: `src/AIUsageMonitor.Ipc/Contracts/UsageQueryContracts.cs`
- Modify: `src/AIUsageMonitor.Ipc/AIUsageMonitor.Ipc.csproj`
- Test: `tests/AIUsageMonitor.Ipc.Tests/Client/AgentUsageQueryClientTests.cs`

**Interfaces:**
- Consumes: `NamedPipeAgentClient.SendAsync(IpcRequest, TimeSpan, CancellationToken)` and existing usage request/response records.
- Produces: `AgentUsageQueryClient : IUsageQueryService`, constructor `(NamedPipeAgentClient client, string profileId, TimeSpan timeout)`, and `ValueTask RefreshAsync(CancellationToken)`.

- [ ] **Step 1: Write failing adapter tests**

Add a transport seam and tests that inject a recording transport. Assert `GetSummaryAsync` sends `UsageQueryCommands.SummaryGet`, uses profile `default`, maps every token field, maps paged projects/models/sessions, throws `AgentQueryException` for an IPC error, and sends `collection.refresh` from `RefreshAsync`.

```csharp
public interface IAgentRequestClient
{
    Task<IpcResponse> SendAsync(IpcRequest request, TimeSpan timeout, CancellationToken cancellationToken);
}

[Fact]
public async Task GetSummaryAsync_MapsRequestAndResponse()
{
    var transport = new RecordingRequestClient(Response(UsageQueryCommands.SummaryGet,
        new UsageSummaryResponse(1, 2, 3, 4, 5, DateTimeOffset.UnixEpoch)));
    var client = new AgentUsageQueryClient(transport, "default", TimeSpan.FromMilliseconds(500));

    UsageSummary result = await client.GetSummaryAsync(Range, CancellationToken.None);

    Assert.Equal(1, result.InputTokens);
    Assert.Equal(UsageQueryCommands.SummaryGet, transport.LastRequest!.Type);
    Assert.Equal("default", transport.LastRequest.Payload.GetProperty("profileId").GetString());
}
```

- [ ] **Step 2: Run tests and confirm the missing types fail compilation**

Run: `dotnet test tests/AIUsageMonitor.Ipc.Tests/AIUsageMonitor.Ipc.Tests.csproj --no-restore`
Expected: FAIL with `IAgentRequestClient` or `AgentUsageQueryClient` not found.

- [ ] **Step 3: Implement request transport, mapping, and protocol errors**

Make `NamedPipeAgentClient` implement `IAgentRequestClient`. Add `CollectionRefresh = "collection.refresh"`. Implement a private generic `SendAsync<TRequest,TResponse>` that serializes with `UsageQueryJson.Options`, verifies `response.Error is null`, deserializes the payload, and maps all four Core result types. Define:

```csharp
public sealed class AgentQueryException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class AgentUsageQueryClient(
    IAgentRequestClient client,
    string profileId,
    TimeSpan timeout) : IUsageQueryService
{
    public ValueTask<UsageSummary> GetSummaryAsync(UsageQueryRange range, CancellationToken cancellationToken);
    public ValueTask<PagedUsageResult<ProjectUsage>> GetProjectsAsync(PagedUsageQuery query, CancellationToken cancellationToken);
    public ValueTask<PagedUsageResult<ModelUsage>> GetModelsAsync(PagedUsageQuery query, CancellationToken cancellationToken);
    public ValueTask<PagedUsageResult<SessionUsage>> GetSessionsAsync(PagedUsageQuery query, CancellationToken cancellationToken);
    public ValueTask RefreshAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Run IPC tests**

Run: `dotnet test tests/AIUsageMonitor.Ipc.Tests/AIUsageMonitor.Ipc.Tests.csproj --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/AIUsageMonitor.Ipc tests/AIUsageMonitor.Ipc.Tests
git commit -m "feat: add typed Agent query client"
```

### Task 2: CLI Argument and Date Contract

**Files:**
- Create: `src/AIUsageMonitor.Cli/CliOptions.cs`
- Test: `tests/AIUsageMonitor.Cli.Tests/CliOptionsTests.cs`

**Interfaces:**
- Consumes: `UsageQueryRange` and `UsagePage`.
- Produces: `CliCommand` enum, `CliOptions`, and `CliParseResult Parse(string[] args, TimeProvider timeProvider, TimeZoneInfo localZone)`.

- [ ] **Step 1: Write failing parser tests**

Cover all five command names, `--from`, `--to`, `--offset`, `--limit`, `--json`, unknown arguments, invalid dates, range reversal, and the current-month default. Use a fixed clock and assert local midnight conversion with `TimeZoneInfo.ConvertTimeToUtc`.

```csharp
[Fact]
public void Parse_DefaultsToCurrentLocalMonth()
{
    var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-18T05:00:00Z"));
    CliParseResult result = CliOptions.Parse(["summary"], clock, KoreaTimeZone);
    Assert.True(result.IsSuccess);
    Assert.Equal(DateTimeOffset.Parse("2026-06-30T15:00:00Z"), result.Options!.Range.FromUtc);
    Assert.Equal(DateTimeOffset.Parse("2026-07-31T15:00:00Z"), result.Options.Range.ToUtc);
}
```

- [ ] **Step 2: Run the focused tests and confirm failure**

Run: `dotnet test tests/AIUsageMonitor.Cli.Tests/AIUsageMonitor.Cli.Tests.csproj --no-restore --filter FullyQualifiedName~CliOptionsTests`
Expected: FAIL because `CliOptions` does not exist.

- [ ] **Step 3: Implement strict parsing**

Use these public shapes and return a human-readable error without throwing for user input:

```csharp
public enum CliCommand { Summary, Projects, Models, Sessions, Refresh }
public sealed record CliOptions(CliCommand Command, UsageQueryRange Range, UsagePage Page, bool Json);
public sealed record CliParseResult(CliOptions? Options, string? Error)
{
    public bool IsSuccess => Options is not null;
}
```

Reject range switches on `refresh`, paging switches on `summary`/`refresh`, duplicate switches, missing values, offset below zero, and limits outside 1..200. Default page to `(0, 50)`.

- [ ] **Step 4: Run parser tests**

Run: `dotnet test tests/AIUsageMonitor.Cli.Tests/AIUsageMonitor.Cli.Tests.csproj --no-restore --filter FullyQualifiedName~CliOptionsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/AIUsageMonitor.Cli/CliOptions.cs tests/AIUsageMonitor.Cli.Tests/CliOptionsTests.cs
git commit -m "feat: parse usage CLI commands"
```

### Task 3: CLI Rendering and Execution

**Files:**
- Create: `src/AIUsageMonitor.Cli/UsageOutputWriter.cs`
- Create: `src/AIUsageMonitor.Cli/CliApplication.cs`
- Modify: `src/AIUsageMonitor.Cli/AgentOrDatabaseQueryClient.cs`
- Modify: `src/AIUsageMonitor.Cli/Program.cs`
- Modify: `src/AIUsageMonitor.Cli/AIUsageMonitor.Cli.csproj`
- Test: `tests/AIUsageMonitor.Cli.Tests/UsageOutputWriterTests.cs`
- Test: `tests/AIUsageMonitor.Cli.Tests/CliApplicationTests.cs`

**Interfaces:**
- Consumes: `CliOptions`, `IUsageQueryService`, and `AgentUsageQueryClient.RefreshAsync` through `ICollectionRefreshClient`.
- Produces: deterministic text/JSON and `Task<int> CliApplication.RunAsync(string[] args, CancellationToken)`.

- [ ] **Step 1: Write failing renderer and application tests**

Assert summary text labels, fixed tabular column order for each list, camel-case JSON property names, invariant numeric formatting, correct service method dispatch, exit 2 plus usage on parse errors, exit 5 plus `agent_unavailable` for failed refresh, exit 1 for unexpected errors, and no stack trace/path leakage.

```csharp
[Fact]
public async Task RunAsync_RefreshUnavailable_ReturnsFive()
{
    var output = new StringWriter();
    var app = CreateApp(refreshException: new TimeoutException(), output: output);
    int code = await app.RunAsync(["refresh"], CancellationToken.None);
    Assert.Equal(5, code);
    Assert.Contains("agent_unavailable", output.ToString());
}
```

- [ ] **Step 2: Run CLI tests and confirm failure**

Run: `dotnet test tests/AIUsageMonitor.Cli.Tests/AIUsageMonitor.Cli.Tests.csproj --no-restore`
Expected: FAIL with missing renderer/application types.

- [ ] **Step 3: Implement output and command execution**

Define the interface below and make `AgentUsageQueryClient` implement it. Have `AgentOrDatabaseQueryClient` retain read fallback and expose mutation exit behavior through the application boundary. `UsageOutputWriter` must use `JsonSerializerDefaults.Web` and write one trailing newline. `CliApplication` parses once, dispatches one service call, and catches only at the outer boundary.

```csharp
public interface ICollectionRefreshClient
{
    ValueTask RefreshAsync(CancellationToken cancellationToken);
}

public sealed class CliApplication(
    IUsageQueryService queries,
    ICollectionRefreshClient refresh,
    TextWriter output,
    TextWriter error,
    TimeProvider timeProvider,
    TimeZoneInfo localZone)
{
    public Task<int> RunAsync(string[] args, CancellationToken cancellationToken);
}
```

Change `Program.cs` to top-level async execution and assign `Environment.ExitCode` from `CliComposition.CreateDefault().RunAsync(args, cancellationToken)`; add the IPC project reference.

- [ ] **Step 4: Run all CLI tests**

Run: `dotnet test tests/AIUsageMonitor.Cli.Tests/AIUsageMonitor.Cli.Tests.csproj --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/AIUsageMonitor.Cli tests/AIUsageMonitor.Cli.Tests
git commit -m "feat: execute and render usage CLI commands"
```

### Task 4: CLI Runtime Composition and Read-Only Fallback

**Files:**
- Create: `src/AIUsageMonitor.Cli/CliComposition.cs`
- Modify: `src/AIUsageMonitor.Infrastructure/Persistence/DatabaseConnectionFactory.cs`
- Modify: `src/AIUsageMonitor.Infrastructure/Queries/SqliteUsageQueryService.cs`
- Test: `tests/AIUsageMonitor.Cli.Tests/CliCompositionTests.cs`
- Test: `tests/AIUsageMonitor.Infrastructure.Tests/Queries/SqliteUsageQueryServiceTests.cs`

**Interfaces:**
- Consumes: current-user pipe identity, default `%LOCALAPPDATA%/AIUsageMonitor/profiles/default/usage.db`, and Task 1 client.
- Produces: `CliComposition.CreateDefault()` with 500 ms Agent timeout and a SQLite read-only fallback that never creates or migrates the database.

- [ ] **Step 1: Write failing composition and read-only tests**

Assert the fallback connection uses `SqliteOpenMode.ReadOnly`, a missing database produces a concise failure rather than creating a file, and default composition builds with profile `default` and the current-user pipe name.

```csharp
[Fact]
public async Task ReadOnlyQuery_DoesNotCreateMissingDatabase()
{
    string path = Path.Combine(temporaryDirectory, "missing.db");
    var service = SqliteUsageQueryService.CreateReadOnly(path);
    await Assert.ThrowsAsync<SqliteException>(() => service.GetSummaryAsync(Range, CancellationToken.None).AsTask());
    Assert.False(File.Exists(path));
}
```

- [ ] **Step 2: Run focused tests and confirm failure**

Run: `dotnet test tests/AIUsageMonitor.Cli.Tests/AIUsageMonitor.Cli.Tests.csproj --no-restore --filter FullyQualifiedName~CliCompositionTests`
Expected: FAIL because `CliComposition` and read-only construction do not exist.

- [ ] **Step 3: Add explicit read-only database construction and default wiring**

Keep Agent's existing read/write factory unchanged. Add `DatabaseConnectionFactory.CreateReadOnly(string)` or an explicit mode constructor, and ensure `SqliteUsageQueryService.CreateReadOnly` uses it. Build the pipe name via `WindowsCurrentUserIdentity` and `UserEndpointIdentity.Create(sid, 1)`.

- [ ] **Step 4: Run CLI and Infrastructure tests**

Run: `dotnet test tests/AIUsageMonitor.Cli.Tests/AIUsageMonitor.Cli.Tests.csproj --no-restore`
Expected: PASS.

Run: `dotnet test tests/AIUsageMonitor.Infrastructure.Tests/AIUsageMonitor.Infrastructure.Tests.csproj --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/AIUsageMonitor.Cli src/AIUsageMonitor.Infrastructure tests/AIUsageMonitor.Cli.Tests tests/AIUsageMonitor.Infrastructure.Tests
git commit -m "feat: wire CLI Agent and database queries"
```

### Task 5: Testable Dashboard ViewModels

**Files:**
- Create: `src/AIUsageMonitor.App/ViewModels/ObservableObject.cs`
- Create: `src/AIUsageMonitor.App/ViewModels/AsyncCommand.cs`
- Create: `src/AIUsageMonitor.App/ViewModels/UsagePageViewModel.cs`
- Create: `src/AIUsageMonitor.App/ViewModels/SummaryViewModel.cs`
- Create: `src/AIUsageMonitor.App/ViewModels/PagedUsageViewModel.cs`
- Create: `src/AIUsageMonitor.App/ViewModels/ProjectsViewModel.cs`
- Create: `src/AIUsageMonitor.App/ViewModels/ModelsViewModel.cs`
- Create: `src/AIUsageMonitor.App/ViewModels/SessionsViewModel.cs`
- Create: `src/AIUsageMonitor.App/ViewModels/SettingsViewModel.cs`
- Create: `src/AIUsageMonitor.App/ViewModels/MainViewModel.cs`
- Test: `tests/AIUsageMonitor.App.Tests/ViewModels/SummaryViewModelTests.cs`
- Test: `tests/AIUsageMonitor.App.Tests/ViewModels/PagedUsageViewModelTests.cs`
- Test: `tests/AIUsageMonitor.App.Tests/ViewModels/SettingsViewModelTests.cs`
- Test: `tests/AIUsageMonitor.App.Tests/ViewModels/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `IUsageQueryService`, `AgentConnectionService`, `StartupTaskService`, and `ICollectionRefreshClient`.
- Produces: bindable ViewModels with `LoadAsync`, cancellation, stale/empty/loading state, paging, serialized refresh, and active-page navigation.

- [ ] **Step 1: Write failing ViewModel tests**

Use controllable query tasks. Cover initial load, `IsLoading`, cancellation preserving the previous rows, empty-state visibility, formatted timestamp, offline stale state, concise protocol error, page next/previous bounds, one in-flight refresh for repeated calls, only active-page auto-load, and explicit StartupTask enable/disable.

```csharp
[Fact]
public async Task LoadAsync_OfflineAfterSuccess_KeepsRowsAndMarksThemStale()
{
    var queries = new SequencedQueries(Summary(10), new TimeoutException());
    var viewModel = new SummaryViewModel(queries, RangeFactory);
    await viewModel.LoadAsync(CancellationToken.None);
    await viewModel.LoadAsync(CancellationToken.None);
    Assert.Equal("10", viewModel.InputTokens);
    Assert.True(viewModel.IsStale);
    Assert.Equal("Agent is offline. Showing the last loaded data.", viewModel.ErrorMessage);
}
```

- [ ] **Step 2: Run App tests and confirm missing ViewModels fail**

Run: `dotnet test tests/AIUsageMonitor.App.Tests/AIUsageMonitor.App.Tests.csproj --no-restore --filter FullyQualifiedName~ViewModels`
Expected: FAIL with missing ViewModel types.

- [ ] **Step 3: Implement focused observable classes**

Use `INotifyPropertyChanged` without an MVVM package. Define the navigation enum and page interface:

```csharp
public enum PageKind { Summary, Projects, Models, Sessions, Settings }

public interface IUsagePageViewModel
{
    bool IsLoading { get; }
    bool IsStale { get; }
    string? ErrorMessage { get; }
    DateTimeOffset? DataUpdatedAt { get; }
    Task LoadAsync(CancellationToken cancellationToken);
}
```

Paged base exposes `Offset`, `Limit = 50`, `TotalCount`, `CanGoPrevious`, `CanGoNext`, `NextAsync`, and `PreviousAsync`. Store immutable row records in `IReadOnlyList<T>`. Treat timeout/IO/access errors as offline stale; clear rows only on successful empty results. Guard refresh with a cached in-flight `Task` and reload only `ActivePage` after successful refresh.

- [ ] **Step 4: Run all App ViewModel tests**

Run: `dotnet test tests/AIUsageMonitor.App.Tests/AIUsageMonitor.App.Tests.csproj --no-restore --filter FullyQualifiedName~ViewModels`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/AIUsageMonitor.App/ViewModels tests/AIUsageMonitor.App.Tests/ViewModels
git commit -m "feat: add dashboard ViewModels"
```

### Task 6: WinUI 3 Application Shell and Pages

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/AIUsageMonitor.App/AIUsageMonitor.App.csproj`
- Replace: `src/AIUsageMonitor.App/Program.cs`
- Create: `src/AIUsageMonitor.App/App.xaml`
- Create: `src/AIUsageMonitor.App/App.xaml.cs`
- Create: `src/AIUsageMonitor.App/MainWindow.xaml`
- Create: `src/AIUsageMonitor.App/MainWindow.xaml.cs`
- Create: `src/AIUsageMonitor.App/Services/AppComposition.cs`
- Create: `src/AIUsageMonitor.App/Views/SummaryPage.xaml`
- Create: `src/AIUsageMonitor.App/Views/SummaryPage.xaml.cs`
- Create: `src/AIUsageMonitor.App/Views/ProjectsPage.xaml`
- Create: `src/AIUsageMonitor.App/Views/ProjectsPage.xaml.cs`
- Create: `src/AIUsageMonitor.App/Views/ModelsPage.xaml`
- Create: `src/AIUsageMonitor.App/Views/ModelsPage.xaml.cs`
- Create: `src/AIUsageMonitor.App/Views/SessionsPage.xaml`
- Create: `src/AIUsageMonitor.App/Views/SessionsPage.xaml.cs`
- Create: `src/AIUsageMonitor.App/Views/SettingsPage.xaml`
- Create: `src/AIUsageMonitor.App/Views/SettingsPage.xaml.cs`
- Test: `tests/AIUsageMonitor.App.Tests/Composition/AppCompositionTests.cs`
- Test: `tests/AIUsageMonitor.App.Tests/Packaging/PackageManifestTests.cs`

**Interfaces:**
- Consumes: Task 1 Agent client and Task 5 ViewModels.
- Produces: a packaged WinUI `Application`, visible `MainWindow`, five NavigationView pages, global status/timestamp/refresh controls, and no App-to-Infrastructure reference.

- [ ] **Step 1: Add failing composition and project-shape tests**

Assert App project has `UseWinUI=true`, references `Microsoft.WindowsAppSDK`, excludes Infrastructure, XAML files contain five navigation tags, StartupTask remains disabled by default, and `AppComposition.Create()` returns non-null ViewModels using Core/IPC services only.

```csharp
[Fact]
public void AppProject_UsesWinUiWithoutInfrastructureReference()
{
    string project = File.ReadAllText(AppProjectPath);
    Assert.Contains("<UseWinUI>true</UseWinUI>", project);
    Assert.Contains("Microsoft.WindowsAppSDK", project);
    Assert.DoesNotContain("AIUsageMonitor.Infrastructure", project);
}
```

- [ ] **Step 2: Run App tests and confirm project-shape failure**

Run: `dotnet test tests/AIUsageMonitor.App.Tests/AIUsageMonitor.App.Tests.csproj --no-restore`
Expected: FAIL because WinUI assets and composition are absent.

- [ ] **Step 3: Configure Windows App SDK and App lifetime**

Pin one stable Windows App SDK version in `Directory.Packages.props`, add `<UseWinUI>true</UseWinUI>` and its package reference, and replace the empty entry point with WinUI generated startup via `App.xaml`. In `OnLaunched`, construct `MainWindow`, call `Activate()`, and begin `MainViewModel.ConnectAndLoadAsync` without blocking the UI thread.

- [ ] **Step 4: Build the shell and five thin views**

Use a left `NavigationView` with tags `summary`, `projects`, `models`, `sessions`, and `settings`. Put connection text, stale indicator, latest timestamp, and refresh button in the shell header. Bind token cards on Summary; bind list columns and paging buttons on Projects/Models/Sessions; bind StartupTask state and an explicit ToggleSwitch on Settings. Code-behind only assigns DataContext and routes navigation.

- [ ] **Step 5: Run App tests and compile x64**

Run: `dotnet test tests/AIUsageMonitor.App.Tests/AIUsageMonitor.App.Tests.csproj --no-restore`
Expected: PASS.

Run: `dotnet build src/AIUsageMonitor.App/AIUsageMonitor.App.csproj -c Debug -r win-x64 --no-restore`
Expected: PASS with 0 warnings and 0 errors.

- [ ] **Step 6: Commit**

```powershell
git add Directory.Packages.props src/AIUsageMonitor.App tests/AIUsageMonitor.App.Tests
git commit -m "feat: add WinUI usage dashboard"
```

### Task 7: Cancellation, Navigation, and Startup Smoke Coverage

**Files:**
- Modify: `src/AIUsageMonitor.App/MainWindow.xaml.cs`
- Modify: `src/AIUsageMonitor.App/ViewModels/MainViewModel.cs`
- Create: `tests/AIUsageMonitor.App.Tests/ViewModels/NavigationLifecycleTests.cs`
- Create: `tests/AIUsageMonitor.App.Tests/Smoke/WinUiStartupTests.cs`

**Interfaces:**
- Consumes: shell navigation and `IUsagePageViewModel.LoadAsync`.
- Produces: cancellation on page change/window close and a Windows-only startup smoke check.

- [ ] **Step 1: Write failing lifecycle tests**

Assert selecting a second page cancels the first page token, selecting the current page does not duplicate loading, close cancels all pending work, and `MainWindow` can be created on an STA thread when Windows App Runtime is available.

```csharp
[Fact]
public async Task NavigateAsync_CancelsPreviousPageLoad()
{
    var first = new BlockingPageViewModel();
    var second = new RecordingPageViewModel();
    var shell = CreateMain(first, second);
    Task firstLoad = shell.NavigateAsync(PageKind.Summary);
    await shell.NavigateAsync(PageKind.Projects);
    Assert.True(first.ObservedCancellation);
}
```

- [ ] **Step 2: Run lifecycle tests and confirm failure**

Run: `dotnet test tests/AIUsageMonitor.App.Tests/AIUsageMonitor.App.Tests.csproj --no-restore --filter "FullyQualifiedName~NavigationLifecycleTests|FullyQualifiedName~WinUiStartupTests"`
Expected: FAIL until cancellation ownership and smoke hook exist.

- [ ] **Step 3: Implement one navigation cancellation source and close disposal**

`MainViewModel.NavigateAsync(PageKind)` must atomically cancel/dispose the old `CancellationTokenSource`, set `ActivePage`, and await its load. `Dispose()` cancels navigation plus refresh. `MainWindow.Closed` calls `Dispose`. Mark the startup smoke test Windows-only and skip only when the runtime bootstrap reports unavailable; do not swallow construction failures.

- [ ] **Step 4: Run lifecycle and full App tests**

Run: `dotnet test tests/AIUsageMonitor.App.Tests/AIUsageMonitor.App.Tests.csproj --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/AIUsageMonitor.App tests/AIUsageMonitor.App.Tests
git commit -m "test: cover dashboard lifecycle"
```

### Task 8: Package, End-to-End Verification, and Run Documentation

**Files:**
- Modify: `packaging/AIUsageMonitor.Package/Package.appxmanifest`
- Modify: `packaging/AIUsageMonitor.Package/AIUsageMonitor.Package.wapproj`
- Create: `README.md`
- Modify: `docs/qa/agent-architecture-checklist.md`
- Test: `tests/AIUsageMonitor.App.Tests/Packaging/PackageManifestTests.cs`

**Interfaces:**
- Consumes: functional App, Agent, and CLI outputs from prior tasks.
- Produces: x64/ARM64 MSIX bundle, documented launch/CLI commands, and final regression evidence.

- [ ] **Step 1: Extend packaging tests before manifest/project edits**

Assert the package entry point still targets the visible App, all three executables are included, architecture outputs are x64 and ARM64, StartupTask is opt-in, and the manifest minimum remains `10.0.22621.0`.

- [ ] **Step 2: Run packaging tests and observe any missing integration**

Run: `dotnet test tests/AIUsageMonitor.App.Tests/AIUsageMonitor.App.Tests.csproj --no-restore --filter FullyQualifiedName~PackageManifestTests`
Expected: FAIL only for newly asserted WinUI/runtime packaging metadata.

- [ ] **Step 3: Complete package metadata and user instructions**

Keep `Executable="AIUsageMonitor.App\AIUsageMonitor.App.exe"`, `EntryPoint="Windows.FullTrustApplication"`, StartupTask disabled, and add only Windows App SDK package metadata required by the successful build. Document:

```powershell
dotnet run --project src/AIUsageMonitor.App/AIUsageMonitor.App.csproj -r win-x64
dotnet run --project src/AIUsageMonitor.Cli/AIUsageMonitor.Cli.csproj -- summary
dotnet run --project src/AIUsageMonitor.Cli/AIUsageMonitor.Cli.csproj -- projects --limit 50
```

Also document MSIX install/Start menu launch and that CLI reads fall back to the local database while refresh requires Agent.

- [ ] **Step 4: Run full test suite**

Run: `dotnet test AIUsageMonitor.sln -c Release --no-restore`
Expected: all tests PASS with 0 warnings and 0 errors.

- [ ] **Step 5: Build both release packages**

Run: `msbuild packaging/AIUsageMonitor.Package/AIUsageMonitor.Package.wapproj /restore /p:Configuration=Release /p:Platform=x64 /p:AppxBundle=Never`
Expected: x64 MSIX build succeeds.

Run: `msbuild packaging/AIUsageMonitor.Package/AIUsageMonitor.Package.wapproj /restore /p:Configuration=Release /p:Platform=ARM64 /p:AppxBundle=Never`
Expected: ARM64 MSIX build succeeds.

Run: `msbuild packaging/AIUsageMonitor.Package/AIUsageMonitor.Package.wapproj /p:Configuration=Release /p:Platform=x64 /p:AppxBundle=Always /p:AppxBundlePlatforms="x64|ARM64"`
Expected: bundle succeeds and contains both architectures.

- [ ] **Step 6: Perform functional smoke checks**

Run the x64 App and verify the visible window opens on Summary, all five destinations navigate, empty/offline states are explicit, and Settings changes StartupTask only after user interaction. Run:

```powershell
dotnet run --project src/AIUsageMonitor.Cli/AIUsageMonitor.Cli.csproj -- summary --json
dotnet run --project src/AIUsageMonitor.Cli/AIUsageMonitor.Cli.csproj -- sessions --limit 25
dotnet run --project src/AIUsageMonitor.Cli/AIUsageMonitor.Cli.csproj -- refresh
```

Expected: the two reads return stable output and refresh returns either 0 with a running Agent or 5 plus `agent_unavailable` without one.

- [ ] **Step 7: Commit**

```powershell
git add packaging README.md docs/qa tests/AIUsageMonitor.App.Tests/Packaging
git commit -m "docs: add Windows app run and package guide"
```

### Task 9: Final Review

**Files:**
- Modify only files required by review findings.

**Interfaces:**
- Consumes: all completed tasks.
- Produces: clean diff, verified release artifacts, and review-ready branch.

- [ ] **Step 1: Inspect scope and forbidden dependencies**

Run: `git diff --check dev...HEAD`
Expected: no whitespace errors.

Run: `rg "AIUsageMonitor.Infrastructure|Microsoft.Data.Sqlite" src/AIUsageMonitor.App`
Expected: no matches.

Run: `rg "Console.WriteLine|Debug.WriteLine" src tests README.md`
Expected: no accidental debug output; intentional CLI output uses injected `TextWriter`.

- [ ] **Step 2: Re-run release verification after any review fix**

Run: `dotnet test AIUsageMonitor.sln -c Release --no-restore`
Expected: all tests PASS.

Run: `dotnet build src/AIUsageMonitor.App/AIUsageMonitor.App.csproj -c Release -r win-x64 --no-restore`
Expected: PASS with 0 warnings and 0 errors.

- [ ] **Step 3: Commit review fixes if needed**

```powershell
git add src/AIUsageMonitor.App src/AIUsageMonitor.Cli src/AIUsageMonitor.Ipc tests
git commit -m "fix: address Windows dashboard review"
```

If no changes are needed, do not create an empty commit.
