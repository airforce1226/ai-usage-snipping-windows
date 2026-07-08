using System.Text.Json;
using AIUsageMonitor.Core.Queries;
using AIUsageMonitor.Ipc.Client;
using AIUsageMonitor.Ipc.Contracts;

namespace AIUsageMonitor.Ipc.Tests.Client;

public sealed class AgentUsageQueryClientTests
{
    private static readonly UsageQueryRange Range = new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1));

    [Fact]
    public async Task GetSummaryAsync_MapsRequestAndResponse()
    {
        var transport = new RecordingRequestClient(Success(UsageQueryCommands.SummaryGet,
            new UsageSummaryResponse(1, 2, 3, 4, 5, DateTimeOffset.UnixEpoch)));
        var client = new AgentUsageQueryClient(transport, "default", TimeSpan.FromMilliseconds(500));

        UsageSummary result = await client.GetSummaryAsync(Range, CancellationToken.None);

        Assert.Equal(new UsageSummary(1, 2, 3, 4, 5, DateTimeOffset.UnixEpoch), result);
        Assert.Equal(UsageQueryCommands.SummaryGet, transport.LastRequest!.Type);
        Assert.Equal("default", transport.LastRequest.Payload.GetProperty("profileId").GetString());
        Assert.Equal(TimeSpan.FromMilliseconds(500), transport.LastTimeout);
    }

    [Fact]
    public async Task GetProjectsAsync_MapsPagedResponse()
    {
        var updatedAt = DateTimeOffset.UnixEpoch.AddHours(1);
        var response = new UsageProjectsResponse(
            [new ProjectUsagePayload("project", 1, 2, 3, 4, 5)], 10, 25, 40, updatedAt);
        var transport = new RecordingRequestClient(Success(UsageQueryCommands.ProjectsGet, response));
        var client = new AgentUsageQueryClient(transport, "default", TimeSpan.FromMilliseconds(500));

        PagedUsageResult<ProjectUsage> result = await client.GetProjectsAsync(
            new PagedUsageQuery(Range, new UsagePage(10, 25)), CancellationToken.None);

        Assert.Equal(40, result.TotalCount);
        Assert.Equal(new ProjectUsage("project", 1, 2, 3, 4, 5), Assert.Single(result.Items));
    }

    [Fact]
    public async Task GetModelsAndSessionsAsync_MapPagedResponses()
    {
        var modelTransport = new RecordingRequestClient(Success(UsageQueryCommands.ModelsGet,
            new UsageModelsResponse([new ModelUsagePayload("gpt", 1, 2, 3, 4, 5)], 0, 50, 1, DateTimeOffset.UnixEpoch)));
        var sessionTransport = new RecordingRequestClient(Success(UsageQueryCommands.SessionsGet,
            new UsageSessionsResponse([new SessionUsagePayload("s", "p", 1, 2, 3, 4, 5, DateTimeOffset.UnixEpoch)], 0, 50, 1, DateTimeOffset.UnixEpoch)));

        PagedUsageResult<ModelUsage> models = await new AgentUsageQueryClient(modelTransport, "default", TimeSpan.FromMilliseconds(500))
            .GetModelsAsync(new PagedUsageQuery(Range, new UsagePage(0, 50)), CancellationToken.None);
        PagedUsageResult<SessionUsage> sessions = await new AgentUsageQueryClient(sessionTransport, "default", TimeSpan.FromMilliseconds(500))
            .GetSessionsAsync(new PagedUsageQuery(Range, new UsagePage(0, 50)), CancellationToken.None);

        Assert.Equal("gpt", Assert.Single(models.Items).Model);
        Assert.Equal("s", Assert.Single(sessions.Items).SessionId);
    }

    [Fact]
    public async Task QueryError_ThrowsCodeBearingException()
    {
        var response = new IpcResponse(1, "request", UsageQueryCommands.SummaryGet, null, new IpcError("profile_not_found", "Profile was not found."));
        var client = new AgentUsageQueryClient(new RecordingRequestClient(response), "missing", TimeSpan.FromMilliseconds(500));

        AgentQueryException exception = await Assert.ThrowsAsync<AgentQueryException>(
            () => client.GetSummaryAsync(Range, CancellationToken.None).AsTask());

        Assert.Equal("profile_not_found", exception.Code);
    }

    [Fact]
    public async Task RefreshAsync_SendsCollectionRefresh()
    {
        var transport = new RecordingRequestClient(Success(CollectionCommands.Refresh, new { accepted = true }));
        var client = new AgentUsageQueryClient(transport, "default", TimeSpan.FromMilliseconds(500));

        await client.RefreshAsync(CancellationToken.None);

        Assert.Equal(CollectionCommands.Refresh, transport.LastRequest!.Type);
    }

    private static IpcResponse Success<T>(string type, T payload) =>
        new(1, "request", type, JsonSerializer.SerializeToElement(payload, UsageQueryJson.Options), null);

    private sealed class RecordingRequestClient(IpcResponse response) : IAgentRequestClient
    {
        public IpcRequest? LastRequest { get; private set; }
        public TimeSpan LastTimeout { get; private set; }

        public Task<IpcResponse> SendAsync(IpcRequest request, TimeSpan timeout, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastTimeout = timeout;
            return Task.FromResult(response with { RequestId = request.RequestId });
        }
    }
}
