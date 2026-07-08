using AIUsageMonitor.App.ViewModels;
using AIUsageMonitor.App.Services;
using AIUsageMonitor.Core.Queries;

namespace AIUsageMonitor.App.Tests.ViewModels;

public sealed class UsageViewModelTests
{
    private static readonly UsageQueryRange Range = new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1));

    [Fact]
    public async Task Summary_LoadAsync_PublishesValuesAndTimestamp()
    {
        var queries = new StubQueries(new UsageSummary(1, 2, 3, 4, 5, DateTimeOffset.UnixEpoch));
        var viewModel = new SummaryViewModel(queries, () => Range);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(1, viewModel.InputTokens);
        Assert.Equal(5, viewModel.EventCount);
        Assert.Equal(DateTimeOffset.UnixEpoch, viewModel.DataUpdatedAt);
        Assert.False(viewModel.IsLoading);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Summary_OfflineAfterSuccess_KeepsDataAndMarksItStale()
    {
        var queries = new StubQueries(
            new UsageSummary(10, 0, 0, 0, 1, DateTimeOffset.UnixEpoch),
            new TimeoutException());
        var viewModel = new SummaryViewModel(queries, () => Range);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(10, viewModel.InputTokens);
        Assert.True(viewModel.IsStale);
        Assert.Equal("Agent is offline. Showing the last loaded data.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Projects_NextAndPrevious_LoadExpectedOffsets()
    {
        var queries = new StubQueries(new UsageSummary(0, 0, 0, 0, 0, DateTimeOffset.UnixEpoch));
        var viewModel = new ProjectsViewModel(queries, () => Range);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.NextAsync(CancellationToken.None);
        await viewModel.PreviousAsync(CancellationToken.None);

        Assert.Equal([0, 50, 0], queries.ProjectOffsets);
        Assert.False(viewModel.CanGoPrevious);
        Assert.True(viewModel.CanGoNext);
        Assert.Single(viewModel.Items);
    }

    [Fact]
    public async Task Projects_SuccessfulEmptyResult_ShowsEmptyState()
    {
        var queries = new StubQueries(new UsageSummary(0, 0, 0, 0, 0, DateTimeOffset.UnixEpoch)) { EmptyProjects = true };
        var viewModel = new ProjectsViewModel(queries, () => Range);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.IsEmpty);
        Assert.Empty(viewModel.Items);
    }

    [Fact]
    public async Task ModelsAndSessions_LoadTheirOwnQueries()
    {
        var queries = new StubQueries(new UsageSummary(0, 0, 0, 0, 0, DateTimeOffset.UnixEpoch));
        var models = new ModelsViewModel(queries, () => Range);
        var sessions = new SessionsViewModel(queries, () => Range);

        await models.LoadAsync(CancellationToken.None);
        await sessions.LoadAsync(CancellationToken.None);

        Assert.Equal("model", Assert.Single(models.Items).Model);
        Assert.Equal("session", Assert.Single(sessions.Items).SessionId);
    }

    [Fact]
    public async Task Settings_SetEnabledAsync_ChangesOnlyAfterExplicitCall()
    {
        var bridge = new RecordingStartupBridge();
        var viewModel = new SettingsViewModel(new StartupTaskService(bridge));

        await viewModel.LoadAsync(CancellationToken.None);
        Assert.Equal(0, bridge.EnableCalls);
        await viewModel.SetEnabledAsync(true, CancellationToken.None);

        Assert.Equal(1, bridge.EnableCalls);
        Assert.True(viewModel.IsEnabled);
    }

    private sealed class StubQueries : IUsageQueryService
    {
        private readonly Queue<object> summaries;
        public StubQueries(params object[] summaries) => this.summaries = new(summaries);
        public List<int> ProjectOffsets { get; } = [];
        public bool EmptyProjects { get; init; }

        public ValueTask<UsageSummary> GetSummaryAsync(UsageQueryRange range, CancellationToken cancellationToken)
        {
            object value = summaries.Dequeue();
            return value is Exception exception ? ValueTask.FromException<UsageSummary>(exception) : ValueTask.FromResult((UsageSummary)value);
        }

        public ValueTask<PagedUsageResult<ProjectUsage>> GetProjectsAsync(PagedUsageQuery query, CancellationToken cancellationToken)
        {
            ProjectOffsets.Add(query.Page.Offset);
            IReadOnlyList<ProjectUsage> items = EmptyProjects ? [] : [new("project", 1, 2, 3, 4, 5)];
            return ValueTask.FromResult(new PagedUsageResult<ProjectUsage>(items, query.Page.Offset, query.Page.Limit, EmptyProjects ? 0 : 101, DateTimeOffset.UnixEpoch));
        }

        public ValueTask<PagedUsageResult<ModelUsage>> GetModelsAsync(PagedUsageQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PagedUsageResult<ModelUsage>([new("model", 1, 2, 3, 4, 5)], query.Page.Offset, query.Page.Limit, 1, DateTimeOffset.UnixEpoch));
        public ValueTask<PagedUsageResult<SessionUsage>> GetSessionsAsync(PagedUsageQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PagedUsageResult<SessionUsage>([new("session", "project", 1, 2, 3, 4, 5, DateTimeOffset.UnixEpoch)], query.Page.Offset, query.Page.Limit, 1, DateTimeOffset.UnixEpoch));
    }

    private sealed class RecordingStartupBridge : IStartupTaskBridge
    {
        public int EnableCalls { get; private set; }
        public ValueTask<StartupTaskState> GetStateAsync(CancellationToken cancellationToken) => ValueTask.FromResult(StartupTaskState.Disabled);
        public ValueTask<StartupTaskState> RequestEnableAsync(CancellationToken cancellationToken)
        {
            EnableCalls++;
            return ValueTask.FromResult(StartupTaskState.Enabled);
        }
        public ValueTask DisableAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
