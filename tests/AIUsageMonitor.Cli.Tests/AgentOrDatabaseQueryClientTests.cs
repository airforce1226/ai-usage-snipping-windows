using AIUsageMonitor.Cli;
using AIUsageMonitor.Core.Queries;

namespace AIUsageMonitor.Cli.Tests;

public sealed class AgentOrDatabaseQueryClientTests
{
    private static readonly UsageQueryRange Range = new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1));

    [Fact]
    public async Task GetSummaryAsync_ReturnsAgentResultWithoutDatabaseFallback()
    {
        var agent = new StubQueries(new UsageSummary(1, 2, 3, 4, 5, DateTimeOffset.UnixEpoch));
        var database = new StubQueries(new UsageSummary(9, 9, 9, 9, 9, DateTimeOffset.UnixEpoch));
        var client = new AgentOrDatabaseQueryClient(agent, database);

        UsageSummary result = await client.GetSummaryAsync(Range, CancellationToken.None);

        Assert.Equal(1, result.InputTokens);
        Assert.Equal(0, database.SummaryCalls);
    }

    [Fact]
    public async Task GetSummaryAsync_FallsBackToReadOnlyDatabaseWhenAgentTimesOut()
    {
        var agent = new StubQueries(new TimeoutException());
        var database = new StubQueries(new UsageSummary(9, 8, 7, 6, 5, DateTimeOffset.UnixEpoch));
        var client = new AgentOrDatabaseQueryClient(agent, database);

        UsageSummary result = await client.GetSummaryAsync(Range, CancellationToken.None);

        Assert.Equal(9, result.InputTokens);
        Assert.Equal(1, database.SummaryCalls);
    }

    [Fact]
    public async Task ExecuteMutationAsync_ReturnsFiveAndNeverUsesDatabaseWhenAgentUnavailable()
    {
        var database = new StubQueries(new UsageSummary(0, 0, 0, 0, 0, DateTimeOffset.UnixEpoch));
        var client = new AgentOrDatabaseQueryClient(new StubQueries(new TimeoutException()), database);

        int exitCode = await client.ExecuteMutationAsync(_ => ValueTask.FromException(new TimeoutException()), CancellationToken.None);

        Assert.Equal(5, exitCode);
        Assert.Equal(0, database.SummaryCalls);
    }

    private sealed class StubQueries : IUsageQueryService
    {
        private readonly UsageSummary? summary;
        private readonly Exception? exception;

        public StubQueries(UsageSummary summary) => this.summary = summary;
        public StubQueries(Exception exception) => this.exception = exception;
        public int SummaryCalls { get; private set; }

        public ValueTask<UsageSummary> GetSummaryAsync(UsageQueryRange range, CancellationToken cancellationToken)
        {
            SummaryCalls++;
            return exception is null ? ValueTask.FromResult(summary!) : ValueTask.FromException<UsageSummary>(exception);
        }

        public ValueTask<PagedUsageResult<ProjectUsage>> GetProjectsAsync(PagedUsageQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<PagedUsageResult<ModelUsage>> GetModelsAsync(PagedUsageQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<PagedUsageResult<SessionUsage>> GetSessionsAsync(PagedUsageQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
