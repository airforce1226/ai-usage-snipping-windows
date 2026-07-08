using AIUsageMonitor.Cli;
using AIUsageMonitor.Core.Queries;
using AIUsageMonitor.Ipc.Client;

namespace AIUsageMonitor.Cli.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsync_SummaryText_WritesStableLabels()
    {
        var output = new StringWriter();
        var app = Create(new StubQueries(), new StubRefresh(), output);

        int code = await app.RunAsync(["summary"], CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Equal("Input tokens:       1\nOutput tokens:      2\nCache read tokens:  3\nCache write tokens: 4\nEvents:             5\nUpdated (UTC):      1970-01-01T00:00:00.0000000+00:00\n", Normalize(output));
    }

    [Fact]
    public async Task RunAsync_ProjectsJson_UsesCamelCaseAndDispatchesQuery()
    {
        var output = new StringWriter();
        var queries = new StubQueries();
        var app = Create(queries, new StubRefresh(), output);

        int code = await app.RunAsync(["projects", "--json"], CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Equal(1, queries.ProjectCalls);
        Assert.Contains("\"projectId\":\"project\"", Normalize(output));
        Assert.Contains("\"totalCount\":1", Normalize(output));
    }

    [Fact]
    public async Task RunAsync_InvalidArguments_ReturnsTwo()
    {
        var output = new StringWriter();
        int code = await Create(new StubQueries(), new StubRefresh(), output)
            .RunAsync(["unknown"], CancellationToken.None);
        Assert.Equal(2, code);
        Assert.Contains("Usage:", Normalize(output));
    }

    [Fact]
    public async Task RunAsync_RefreshUnavailable_ReturnsFive()
    {
        var output = new StringWriter();
        int code = await Create(new StubQueries(), new StubRefresh(new TimeoutException()), output)
            .RunAsync(["refresh"], CancellationToken.None);
        Assert.Equal(5, code);
        Assert.Contains("agent_unavailable", Normalize(output));
    }

    [Fact]
    public async Task RunAsync_UnexpectedFailure_ReturnsOneWithoutStackTrace()
    {
        var output = new StringWriter();
        int code = await Create(new StubQueries(new InvalidOperationException("broken")), new StubRefresh(), output)
            .RunAsync(["summary"], CancellationToken.None);
        Assert.Equal(1, code);
        Assert.Equal("unexpected_error: broken\n", Normalize(output));
    }

    private static CliApplication Create(IUsageQueryService queries, ICollectionRefreshClient refresh, StringWriter output) =>
        new(queries, refresh, output, output,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-07-18T00:00:00Z")),
            TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time"));

    private static string Normalize(StringWriter writer) => writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);

    private sealed class StubRefresh(Exception? exception = null) : ICollectionRefreshClient
    {
        public ValueTask RefreshAsync(CancellationToken cancellationToken) =>
            exception is null ? ValueTask.CompletedTask : ValueTask.FromException(exception);
    }

    private sealed class StubQueries(Exception? exception = null) : IUsageQueryService
    {
        public int ProjectCalls { get; private set; }
        public ValueTask<UsageSummary> GetSummaryAsync(UsageQueryRange range, CancellationToken cancellationToken) =>
            exception is null
                ? ValueTask.FromResult(new UsageSummary(1, 2, 3, 4, 5, DateTimeOffset.UnixEpoch))
                : ValueTask.FromException<UsageSummary>(exception);
        public ValueTask<PagedUsageResult<ProjectUsage>> GetProjectsAsync(PagedUsageQuery query, CancellationToken cancellationToken)
        {
            ProjectCalls++;
            return ValueTask.FromResult(new PagedUsageResult<ProjectUsage>([new("project", 1, 2, 3, 4, 5)], 0, 50, 1, DateTimeOffset.UnixEpoch));
        }
        public ValueTask<PagedUsageResult<ModelUsage>> GetModelsAsync(PagedUsageQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PagedUsageResult<ModelUsage>([new("model", 1, 2, 3, 4, 5)], 0, 50, 1, DateTimeOffset.UnixEpoch));
        public ValueTask<PagedUsageResult<SessionUsage>> GetSessionsAsync(PagedUsageQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PagedUsageResult<SessionUsage>([new("session", "project", 1, 2, 3, 4, 5, DateTimeOffset.UnixEpoch)], 0, 50, 1, DateTimeOffset.UnixEpoch));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
