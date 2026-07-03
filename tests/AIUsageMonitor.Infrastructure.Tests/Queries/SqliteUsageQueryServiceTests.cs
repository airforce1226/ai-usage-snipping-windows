using AIUsageMonitor.Core.Domain;
using AIUsageMonitor.Core.Queries;
using AIUsageMonitor.Infrastructure.Persistence;
using AIUsageMonitor.Infrastructure.Queries;
using Microsoft.Data.Sqlite;

namespace AIUsageMonitor.Infrastructure.Tests.Queries;

public sealed class SqliteUsageQueryServiceTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"AIUsageMonitor-Query-{Guid.NewGuid():N}");
    private readonly string databasePath;
    private readonly DateTimeOffset from = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
    private readonly DateTimeOffset to = DateTimeOffset.Parse("2026-07-02T00:00:00Z");
    private readonly DateTimeOffset updated = DateTimeOffset.Parse("2026-07-01T23:00:00.789Z");

    public SqliteUsageQueryServiceTests()
    {
        Directory.CreateDirectory(temporaryDirectory);
        databasePath = Path.Combine(temporaryDirectory, "usage.db");
    }

    [Fact]
    public async Task SummaryAsync_AggregatesHalfOpenRangeAndReportsLatestUpdate()
    {
        var service = await SeedAsync();

        var result = await service.GetSummaryAsync(new UsageQueryRange(from, to), CancellationToken.None);

        Assert.Equal(new UsageSummary(18, 8, 6, 4, 3, to), result);
    }

    [Fact]
    public async Task Breakdowns_AreStableAndPaged()
    {
        var service = await SeedAsync();
        var query = new PagedUsageQuery(new UsageQueryRange(from, to), new UsagePage(0, 2));

        var projects = await service.GetProjectsAsync(query, CancellationToken.None);
        var models = await service.GetModelsAsync(query, CancellationToken.None);
        var sessions = await service.GetSessionsAsync(query, CancellationToken.None);

        Assert.Equal(["project-a", "project-b"], projects.Items.Select(x => x.ProjectId));
        Assert.Equal(3, projects.TotalCount);
        Assert.Equal(["model-a", "model-b"], models.Items.Select(x => x.Model));
        Assert.Equal(3, models.TotalCount);
        Assert.Equal(["session-c", "session-b"], sessions.Items.Select(x => x.SessionId));
        Assert.Equal(3, sessions.TotalCount);
        Assert.All(new[] { projects.DatabaseUpdatedAtUtc, models.DatabaseUpdatedAtUtc, sessions.DatabaseUpdatedAtUtc }, value => Assert.Equal(to, value));
    }

    [Fact]
    public async Task Sessions_GroupAcrossProjectsAndSelectLatestProjectDeterministically()
    {
        var service = await SeedAsync(includeSharedSession: true);
        var result = await service.GetSessionsAsync(
            new PagedUsageQuery(new UsageQueryRange(from, to), new UsagePage(0, 10)),
            CancellationToken.None);

        var shared = Assert.Single(result.Items, item => item.SessionId == "session-shared");
        Assert.Equal(3, shared.EventCount);
        Assert.Equal(6, shared.InputTokens);
        Assert.Equal(ExpectedTieProject(), shared.ProjectId);
        Assert.Equal(4, result.TotalCount);
    }

    [Fact]
    public async Task Sessions_SecondPagePreservesStableOrderAndMetadata()
    {
        var service = await SeedAsync(includeSharedSession: true);
        var result = await service.GetSessionsAsync(
            new PagedUsageQuery(new UsageQueryRange(from, to), new UsagePage(2, 2)),
            CancellationToken.None);

        Assert.Equal(["session-b", "session-a"], result.Items.Select(item => item.SessionId));
        Assert.Equal(2, result.Offset);
        Assert.Equal(2, result.Limit);
        Assert.Equal(4, result.TotalCount);
    }

    [Fact]
    public void ConnectionOptions_AreReadOnlyAndShared()
    {
        var service = new SqliteUsageQueryService(databasePath);
        var options = new SqliteConnectionStringBuilder(service.ConnectionString);

        Assert.Equal(SqliteOpenMode.ReadOnly, options.Mode);
        Assert.Equal(SqliteCacheMode.Shared, options.Cache);
    }

    [Fact]
    public async Task QueryAsync_ObservesCancellation()
    {
        var service = await SeedAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await service.GetSummaryAsync(new UsageQueryRange(from, to), cancellation.Token));
    }

    private async Task<SqliteUsageQueryService> SeedAsync(bool includeSharedSession = false)
    {
        var factory = new DatabaseConnectionFactory(databasePath);
        await new DatabaseMigrator(factory).InitializeAsync(CancellationToken.None);
        var store = new SqliteUsageEventStore(factory);
        var events = new[]
        {
            Event("session-a", "project-a", "model-a", from.AddHours(1), 10, 2, 3, 1, 0),
            Event("session-b", "project-b", "model-b", from.AddHours(2), 5, 5, 2, 2, 10),
            Event("session-c", "project-c", "model-c", from.AddHours(3), 3, 1, 1, 1, 20),
            Event("session-out", "project-a", "model-a", to, 100, 100, 100, 100, 30),
        };
        await store.UpsertBatchAsync(events, new SourceCheckpoint("C:/logs/a.jsonl", 50, 50, updated, "v1"), CancellationToken.None);
        if (includeSharedSession)
        {
            var tieTimestamp = from.AddHours(4);
            await store.UpsertBatchAsync(
                [
                    Event("session-shared", "project-old", "model-a", from.AddMinutes(30), 1, 0, 0, 0, 100),
                    Event("session-shared", "project-tie-a", "model-a", tieTimestamp, 2, 0, 0, 0, 110),
                    Event("session-shared", "project-tie-b", "model-a", tieTimestamp, 3, 0, 0, 0, 120),
                ],
                new SourceCheckpoint("C:/logs/a.jsonl", 150, 150, updated, "v1"),
                CancellationToken.None);
        }
        return new SqliteUsageQueryService(databasePath);
    }

    private string ExpectedTieProject()
    {
        var timestamp = from.AddHours(4);
        var first = Event("session-shared", "project-tie-a", "model-a", timestamp, 2, 0, 0, 0, 110);
        var second = Event("session-shared", "project-tie-b", "model-a", timestamp, 3, 0, 0, 0, 120);
        return string.CompareOrdinal(first.StableKey, second.StableKey) > 0 ? first.ProjectId : second.ProjectId;
    }

    private static UsageEvent Event(string session, string project, string model, DateTimeOffset timestamp, long input, long output, long read, long write, long offset) =>
        UsageEvent.Create(ProviderKind.Claude, session, null, project, timestamp, model, new TokenUsage(input, output, read, write), new SourceLocation("C:/logs/a.jsonl", offset));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(temporaryDirectory, true);
    }
}
