using AIUsageMonitor.Core.Abstractions;
using AIUsageMonitor.Core.Domain;
using AIUsageMonitor.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace AIUsageMonitor.Infrastructure.Tests.Persistence;

public sealed class DatabasePersistenceTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"AIUsageMonitor-Db-{Guid.NewGuid():N}");

    public DatabasePersistenceTests()
    {
        Directory.CreateDirectory(temporaryDirectory);
    }

    [Fact]
    public async Task InitializeAsync_CreatesSchemaInWalMode()
    {
        var factory = new DatabaseConnectionFactory(DatabasePath());
        var migrator = new DatabaseMigrator(factory);

        await migrator.InitializeAsync(CancellationToken.None);

        await using var connection = await factory.OpenAsync(CancellationToken.None);
        await using var journalCommand = connection.CreateCommand();
        journalCommand.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", await journalCommand.ExecuteScalarAsync());

        await using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
        await using var reader = await tableCommand.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        Assert.Contains("usage_events", names);
        Assert.Contains("source_checkpoints", names);
        Assert.Contains("schema_versions", names);
    }

    [Fact]
    public async Task UpsertBatchAsync_InsertsEventOnceAndUpdatesCheckpointAtomically()
    {
        var factory = new DatabaseConnectionFactory(DatabasePath());
        await new DatabaseMigrator(factory).InitializeAsync(CancellationToken.None);
        var store = new SqliteUsageEventStore(factory);
        var usageEvent = UsageEvent.Create(
            ProviderKind.Claude,
            "session-1",
            "message-1",
            "project-1",
            DateTimeOffset.Parse("2026-07-01T01:00:00Z"),
            "claude-sonnet-4-6",
            new TokenUsage(10, 2, 3, 1),
            new SourceLocation("C:/logs/a.jsonl", 0));
        var checkpoint = new SourceCheckpoint(
            "C:/logs/a.jsonl",
            256,
            300,
            DateTimeOffset.Parse("2026-07-01T01:01:00Z"),
            "claude-v1",
            new Dictionary<string, string> { ["state"] = "ok" });

        var firstInsert = await store.UpsertBatchAsync(
            [usageEvent], checkpoint, CancellationToken.None);
        var duplicateInsert = await store.UpsertBatchAsync(
            [usageEvent], checkpoint, CancellationToken.None);
        var storedCheckpoint = await store.GetAsync(
            "C:/logs/a.jsonl", CancellationToken.None);

        Assert.Equal(1, firstInsert);
        Assert.Equal(0, duplicateInsert);
        Assert.NotNull(storedCheckpoint);
        Assert.Equal(checkpoint.CanonicalPath, storedCheckpoint.CanonicalPath);
        Assert.Equal(checkpoint.ByteOffset, storedCheckpoint.ByteOffset);
        Assert.Equal(checkpoint.FileLength, storedCheckpoint.FileLength);
        Assert.Equal(checkpoint.LastWriteTimeUtc, storedCheckpoint.LastWriteTimeUtc);
        Assert.Equal(checkpoint.ParserVersion, storedCheckpoint.ParserVersion);
        Assert.Equal("ok", storedCheckpoint.ProviderState!["state"]);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(temporaryDirectory, recursive: true);
    }

    private string DatabasePath() => Path.Combine(temporaryDirectory, "usage.db");
}
