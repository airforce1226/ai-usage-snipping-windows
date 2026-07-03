using System.Text.Json;
using AIUsageMonitor.Agent;
using AIUsageMonitor.Core.Collection;
using AIUsageMonitor.Core.Queries;
using AIUsageMonitor.Ipc.Contracts;

namespace AIUsageMonitor.Agent.Tests;

public sealed class AgentQueryHandlerTests
{
    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddDays(1);
    private static readonly DateTimeOffset Updated = To.AddHours(1);

    [Fact]
    public async Task SummaryMapsAllFieldsAndCorrelation()
    {
        var service = new RecordingQueryService { Summary = new(1, 2, 3, 4, 5, Updated) };
        var response = await Handler(service).HandleAsync(Request(UsageQueryCommands.SummaryGet, new UsageSummaryRequest("default", From, To), "summary-id"), default);
        var payload = response.Payload!.Value.Deserialize<UsageSummaryResponse>(UsageQueryJson.Options)!;
        Assert.Equal(new UsageSummaryResponse(1, 2, 3, 4, 5, Updated), payload);
        Assert.Equal((1, "summary-id", UsageQueryCommands.SummaryGet), (response.ProtocolVersion, response.RequestId, response.Type));
        Assert.Equal(1, service.SummaryCalls);
        Assert.Equal(new UsageQueryRange(From, To), service.Range);
    }

    [Fact]
    public async Task ProjectsMapsPagingCountAndItems()
    {
        var service = new RecordingQueryService { Projects = new([new("project", 1, 2, 3, 4, 5)], 7, 8, 9, Updated) };
        var response = await Handler(service).HandleAsync(Request(UsageQueryCommands.ProjectsGet, new UsageProjectsRequest("default", From, To, 7, 8)), default);
        var payload = response.Payload!.Value.Deserialize<UsageProjectsResponse>(UsageQueryJson.Options)!;
        Assert.Equal(new ProjectUsagePayload("project", 1, 2, 3, 4, 5), Assert.Single(payload.Items));
        Assert.Equal((7, 8, 9, Updated), (payload.Offset, payload.Limit, payload.TotalCount, payload.DatabaseUpdatedAtUtc));
        Assert.Equal(1, service.ProjectCalls);
    }

    [Fact]
    public async Task ModelsMapsPagingCountAndItems()
    {
        var service = new RecordingQueryService { Models = new([new("model", 1, 2, 3, 4, 5)], 7, 8, 9, Updated) };
        var response = await Handler(service).HandleAsync(Request(UsageQueryCommands.ModelsGet, new UsageModelsRequest("default", From, To, 7, 8)), default);
        var payload = response.Payload!.Value.Deserialize<UsageModelsResponse>(UsageQueryJson.Options)!;
        Assert.Equal(new ModelUsagePayload("model", 1, 2, 3, 4, 5), Assert.Single(payload.Items));
        Assert.Equal((7, 8, 9, Updated), (payload.Offset, payload.Limit, payload.TotalCount, payload.DatabaseUpdatedAtUtc));
        Assert.Equal(1, service.ModelCalls);
    }

    [Fact]
    public async Task SessionsMapsPagingCountItemsAndTimestamp()
    {
        var lastEvent = To.AddMinutes(-1);
        var service = new RecordingQueryService { Sessions = new([new("session", "project", 1, 2, 3, 4, 5, lastEvent)], 7, 8, 9, Updated) };
        var response = await Handler(service).HandleAsync(Request(UsageQueryCommands.SessionsGet, new UsageSessionsRequest("default", From, To, 7, 8)), default);
        var payload = response.Payload!.Value.Deserialize<UsageSessionsResponse>(UsageQueryJson.Options)!;
        Assert.Equal(new SessionUsagePayload("session", "project", 1, 2, 3, 4, 5, lastEvent), Assert.Single(payload.Items));
        Assert.Equal((7, 8, 9, Updated), (payload.Offset, payload.Limit, payload.TotalCount, payload.DatabaseUpdatedAtUtc));
        Assert.Equal(1, service.SessionCalls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"profileId\":\"default\",\"fromUtc\":\"2026-01-01T00:00:00Z\",\"toUtc\":\"2026-01-02T00:00:00Z\",\"sql\":\"select 1\"}")]
    [InlineData("\"wrong-shape\"")]
    public async Task MalformedWrongShapeNullOrExtraPayloadReturnsInvalidPayload(string json)
    {
        var service = new RecordingQueryService();
        var response = await Handler(service).HandleAsync(RawRequest(UsageQueryCommands.SummaryGet, json), default);
        Assert.Equal("invalid_payload", response.Error?.Code);
        Assert.Equal(0, service.TotalCalls);
    }

    [Theory]
    [InlineData("", "usage.invalid_profile_id")]
    [InlineData("default", "usage.invalid_range")]
    public async Task ValidationReturnsStableError(string profileId, string errorCode)
    {
        var service = new RecordingQueryService();
        var request = new UsageSummaryRequest(profileId, From, profileId.Length == 0 ? To : From);
        var response = await Handler(service).HandleAsync(Request(UsageQueryCommands.SummaryGet, request), default);
        Assert.Equal(errorCode, response.Error?.Code);
        Assert.Equal(0, service.TotalCalls);
    }

    [Fact]
    public async Task ValidNonDefaultProfileReturnsProfileNotFound()
    {
        var service = new RecordingQueryService();
        var response = await Handler(service).HandleAsync(Request(UsageQueryCommands.SummaryGet, new UsageSummaryRequest("other", From, To)), default);
        Assert.Equal("profile_not_found", response.Error?.Code);
        Assert.Equal(0, service.TotalCalls);
    }

    [Fact]
    public async Task QueryReceivesCancellationTokenAndCancellationPropagates()
    {
        var service = new RecordingQueryService { CancelSummary = true };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Handler(service).HandleAsync(Request(UsageQueryCommands.SummaryGet, new UsageSummaryRequest("default", From, To)), cancellation.Token));
        Assert.Equal(cancellation.Token, service.Token);
    }

    private static AgentRequestHandler Handler(RecordingQueryService service) => new(Runtime(), service);
    private static AgentRuntime Runtime() => new(new NoOpCollection(), () => DateTimeOffset.UtcNow, new NoOpShutdown());
    private static IpcRequest Request<T>(string type, T payload, string id = "request-id") => new(1, id, type, JsonSerializer.SerializeToElement(payload, UsageQueryJson.Options));
    private static IpcRequest RawRequest(string type, string json) => new(1, "request-id", type, JsonDocument.Parse(json).RootElement.Clone());

    private sealed class NoOpCollection : ICollectionControl
    {
        public ValueTask PauseAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask ResumeAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask RefreshAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DrainAsync(TimeSpan timeout, CancellationToken token) => ValueTask.CompletedTask;
    }
    private sealed class NoOpShutdown : IHostShutdownSignal { public void Signal() { } }

    private sealed class RecordingQueryService : IUsageQueryService
    {
        public UsageSummary Summary { get; init; } = new(0, 0, 0, 0, 0, Updated);
        public PagedUsageResult<ProjectUsage> Projects { get; init; } = new([], 0, 1, 0, Updated);
        public PagedUsageResult<ModelUsage> Models { get; init; } = new([], 0, 1, 0, Updated);
        public PagedUsageResult<SessionUsage> Sessions { get; init; } = new([], 0, 1, 0, Updated);
        public bool CancelSummary { get; init; }
        public int SummaryCalls { get; private set; }
        public int ProjectCalls { get; private set; }
        public int ModelCalls { get; private set; }
        public int SessionCalls { get; private set; }
        public int TotalCalls => SummaryCalls + ProjectCalls + ModelCalls + SessionCalls;
        public UsageQueryRange? Range { get; private set; }
        public CancellationToken Token { get; private set; }
        public ValueTask<UsageSummary> GetSummaryAsync(UsageQueryRange range, CancellationToken token) { SummaryCalls++; Range = range; Token = token; if (CancelSummary) token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Summary); }
        public ValueTask<PagedUsageResult<ProjectUsage>> GetProjectsAsync(PagedUsageQuery query, CancellationToken token) { ProjectCalls++; Token = token; return ValueTask.FromResult(Projects); }
        public ValueTask<PagedUsageResult<ModelUsage>> GetModelsAsync(PagedUsageQuery query, CancellationToken token) { ModelCalls++; Token = token; return ValueTask.FromResult(Models); }
        public ValueTask<PagedUsageResult<SessionUsage>> GetSessionsAsync(PagedUsageQuery query, CancellationToken token) { SessionCalls++; Token = token; return ValueTask.FromResult(Sessions); }
    }
}
