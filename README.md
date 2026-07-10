# AI Usage Monitor for Windows

Windows 11용 로컬 AI 사용량 모니터입니다. 백그라운드 Agent가 사용량을 수집하고, WinUI 대시보드와 `ai-usage-status` CLI가 같은 조회 프로토콜로 데이터를 보여줍니다.

## Requirements

- Windows 11 23H2 이상
- .NET 8 SDK
- Visual Studio 2022 Build Tools with MSIX/Windows App SDK build support

## Run the dashboard during development

```powershell
dotnet run --project src/AIUsageMonitor.App/AIUsageMonitor.App.csproj -r win-x64
```

대시보드는 Summary, Projects, Models, Sessions, Settings 화면을 제공합니다. Agent가 아직 실행 중이 아니거나 데이터베이스가 없으면 오프라인/빈 상태가 표시됩니다.

## Run the CLI during development

```powershell
dotnet run --project src/AIUsageMonitor.Cli/AIUsageMonitor.Cli.csproj -- summary
dotnet run --project src/AIUsageMonitor.Cli/AIUsageMonitor.Cli.csproj -- summary --json
dotnet run --project src/AIUsageMonitor.Cli/AIUsageMonitor.Cli.csproj -- projects --limit 50
dotnet run --project src/AIUsageMonitor.Cli/AIUsageMonitor.Cli.csproj -- sessions --limit 25
dotnet run --project src/AIUsageMonitor.Cli/AIUsageMonitor.Cli.csproj -- refresh
```

CLI 읽기 명령(`summary`, `projects`, `models`, `sessions`)은 Agent IPC를 먼저 시도하고, 실패하면 기본 프로필의 로컬 SQLite 데이터베이스를 읽기 전용으로 조회합니다. `refresh`는 Agent가 실행 중이어야 하며, Agent를 사용할 수 없으면 exit code `5`와 `agent_unavailable` 메시지를 반환합니다.

공통 옵션:

- `--from yyyy-MM-dd`
- `--to yyyy-MM-dd`
- `--offset n`
- `--limit n` (`1..200`)
- `--json`

## Build and package

x64 또는 ARM64 MSIX 패키지:

```powershell
msbuild packaging/AIUsageMonitor.Package/AIUsageMonitor.Package.wapproj /restore /p:Configuration=Release /p:Platform=x64 /p:AppxBundle=Never
msbuild packaging/AIUsageMonitor.Package/AIUsageMonitor.Package.wapproj /restore /p:Configuration=Release /p:Platform=ARM64 /p:AppxBundle=Never
```

x64/ARM64 번들:

```powershell
msbuild packaging/AIUsageMonitor.Package/AIUsageMonitor.Package.wapproj /p:Configuration=Release /p:Platform=x64 /p:AppxBundle=Always /p:AppxBundlePlatforms="x64|ARM64"
```

패키지 산출물은 `packaging/AIUsageMonitor.Package/AppPackages/` 아래에 생성됩니다. 개발용 인증서가 포함된 테스트 패키지는 `Install.ps1`로 설치한 뒤 Start 메뉴에서 `AI Usage Monitor`를 실행합니다.

## Verify

```powershell
dotnet test AIUsageMonitor.sln -c Release --no-restore
dotnet build src/AIUsageMonitor.App/AIUsageMonitor.App.csproj -c Release -r win-x64 --no-restore
```

패키징 검증은 위의 MSBuild 명령으로 x64, ARM64, x64/ARM64 번들을 각각 빌드해 확인합니다.
