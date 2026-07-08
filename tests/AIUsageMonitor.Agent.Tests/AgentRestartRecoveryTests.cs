using System.Text;
using System.Text.Json;
using AIUsageMonitor.Core.Domain;
using AIUsageMonitor.Core.Queries;
using AIUsageMonitor.Infrastructure.Collection;
using AIUsageMonitor.Infrastructure.Persistence;
using AIUsageMonitor.Infrastructure.Providers.Claude;
using AIUsageMonitor.Infrastructure.Queries;
using Microsoft.Data.Sqlite;

namespace AIUsageMonitor.Agent.Tests;

public sealed class AgentRestartRecoveryTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"AIUsageMonitor-recovery-{Guid.NewGuid():N}");

    [Fact]
    public async Task RestartContinuesFromCheckpointWithoutDuplicateEvents()
    {
        string sourceRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "claude")).FullName;
        string sourcePath = Path.Combine(sourceRoot, "session.jsonl");
        string databasePath = Path.Combine(temporaryDirectory, "usage.db");
        await File.WriteAllTextAsync(sourcePath, ClaudeLine("first") + Environment.NewLine, Encoding.UTF8);
        var connectionFactory = new DatabaseConnectionFactory(databasePath);
        await new DatabaseMigrator(connectionFactory).InitializeAsync(CancellationToken.None);

        await RefreshOnceAsync(sourceRoot, connectionFactory);
        await File.AppendAllTextAsync(sourcePath, ClaudeLine("second") + Environment.NewLine, Encoding.UTF8);
        await RefreshOnceAsync(sourceRoot, connectionFactory);
        await RefreshOnceAsync(sourceRoot, connectionFactory);

        var queries = new SqliteUsageQueryService(databasePath);
        UsageSummary summary = await queries.GetSummaryAsync(
            new UsageQueryRange(DateTimeOffset.Parse("2026-07-01T00:00:00Z"), DateTimeOffset.Parse("2026-07-02T00:00:00Z")),
            CancellationToken.None);

        Assert.Equal(2, summary.EventCount);
        Assert.Equal(2, summary.InputTokens);
        Assert.Equal(2, summary.OutputTokens);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true);
    }

    private static async Task RefreshOnceAsync(string sourceRoot, DatabaseConnectionFactory connectionFactory)
    {
        var store = new SqliteUsageEventStore(connectionFactory);
        await using var coordinator = new CollectionCoordinator(
            [new ProviderCollectionSource(ProviderKind.Claude, sourceRoot, "project", "v1", new ClaudeUsageRecordParser())],
            new IncrementalFileReader(store, store),
            new ProviderFileWatcherFactory());
        await coordinator.RefreshAsync(CancellationToken.None);
    }

    private static string ClaudeLine(string messageId) => JsonSerializer.Serialize(new
    {
        type = "assistant",
        uuid = messageId,
        sessionId = "session",
        cwd = "C:\\work",
        timestamp = "2026-07-01T01:00:00Z",
        message = new { model = "claude", usage = new { input_tokens = 1, output_tokens = 1 } },
    });
}
