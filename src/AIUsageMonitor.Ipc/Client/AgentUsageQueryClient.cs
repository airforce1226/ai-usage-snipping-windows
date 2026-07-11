using System.Text.Json;
using AIUsageMonitor.Core.Queries;
using AIUsageMonitor.Ipc.Contracts;

namespace AIUsageMonitor.Ipc.Client;

public interface IAgentRequestClient
{
    Task<IpcResponse> SendAsync(IpcRequest request, TimeSpan timeout, CancellationToken cancellationToken);
}

public interface ICollectionRefreshClient
{
    ValueTask RefreshAsync(CancellationToken cancellationToken);
}

public sealed class AgentQueryException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class AgentUsageQueryClient(IAgentRequestClient client, string profileId, TimeSpan timeout)
    : IUsageQueryService, ICollectionRefreshClient
{
    public async ValueTask<UsageSummary> GetSummaryAsync(UsageQueryRange range, CancellationToken cancellationToken)
    {
        UsageSummaryResponse value = await SendAsync<UsageSummaryRequest, UsageSummaryResponse>(UsageQueryCommands.SummaryGet,
            new(profileId, range.FromUtc, range.ToUtc), cancellationToken).ConfigureAwait(false);
        return new(value.InputTokens, value.OutputTokens, value.CacheReadTokens, value.CacheWriteTokens, value.EventCount, value.DatabaseUpdatedAtUtc);
    }

    public async ValueTask<PagedUsageResult<ProjectUsage>> GetProjectsAsync(PagedUsageQuery query, CancellationToken cancellationToken)
    {
        UsageProjectsResponse value = await SendAsync<UsageProjectsRequest, UsageProjectsResponse>(UsageQueryCommands.ProjectsGet,
            new(profileId, query.Range.FromUtc, query.Range.ToUtc, query.Page.Offset, query.Page.Limit), cancellationToken).ConfigureAwait(false);
        return new(value.Items.Select(x => new ProjectUsage(x.ProjectId, x.InputTokens, x.OutputTokens, x.CacheReadTokens, x.CacheWriteTokens, x.EventCount)).ToArray(), value.Offset, value.Limit, value.TotalCount, value.DatabaseUpdatedAtUtc);
    }

    public async ValueTask<PagedUsageResult<ModelUsage>> GetModelsAsync(PagedUsageQuery query, CancellationToken cancellationToken)
    {
        UsageModelsResponse value = await SendAsync<UsageModelsRequest, UsageModelsResponse>(UsageQueryCommands.ModelsGet,
            new(profileId, query.Range.FromUtc, query.Range.ToUtc, query.Page.Offset, query.Page.Limit), cancellationToken).ConfigureAwait(false);
        return new(value.Items.Select(x => new ModelUsage(x.Model, x.InputTokens, x.OutputTokens, x.CacheReadTokens, x.CacheWriteTokens, x.EventCount)).ToArray(), value.Offset, value.Limit, value.TotalCount, value.DatabaseUpdatedAtUtc);
    }

    public async ValueTask<PagedUsageResult<SessionUsage>> GetSessionsAsync(PagedUsageQuery query, CancellationToken cancellationToken)
    {
        UsageSessionsResponse value = await SendAsync<UsageSessionsRequest, UsageSessionsResponse>(UsageQueryCommands.SessionsGet,
            new(profileId, query.Range.FromUtc, query.Range.ToUtc, query.Page.Offset, query.Page.Limit), cancellationToken).ConfigureAwait(false);
        return new(value.Items.Select(x => new SessionUsage(x.SessionId, x.ProjectId, x.InputTokens, x.OutputTokens, x.CacheReadTokens, x.CacheWriteTokens, x.EventCount, x.LastEventUtc)).ToArray(), value.Offset, value.Limit, value.TotalCount, value.DatabaseUpdatedAtUtc);
    }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken) =>
        _ = await SendAsync<object, JsonElement>(CollectionCommands.Refresh, new { }, cancellationToken).ConfigureAwait(false);

    private async ValueTask<TResponse> SendAsync<TRequest, TResponse>(string command, TRequest payload, CancellationToken cancellationToken)
    {
        var request = new IpcRequest(1, Guid.NewGuid().ToString("N"), command, JsonSerializer.SerializeToElement(payload, UsageQueryJson.Options));
        IpcResponse response = await client.SendAsync(request, timeout, cancellationToken).ConfigureAwait(false);
        if (response.Error is not null) throw new AgentQueryException(response.Error.Code, response.Error.Message);
        if (response.Payload is null) throw new AgentQueryException("invalid_response", "Agent response did not include a payload.");
        return response.Payload.Value.Deserialize<TResponse>(UsageQueryJson.Options)
            ?? throw new AgentQueryException("invalid_response", "Agent response payload was invalid.");
    }
}
