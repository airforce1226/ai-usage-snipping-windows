using AIUsageMonitor.Core.Queries;

namespace AIUsageMonitor.Cli;

public sealed class AgentOrDatabaseQueryClient(
    IUsageQueryService agentQueries,
    IUsageQueryService readOnlyDatabaseQueries) : IUsageQueryService
{
    public ValueTask<UsageSummary> GetSummaryAsync(UsageQueryRange range, CancellationToken cancellationToken) =>
        QueryAsync(
            token => agentQueries.GetSummaryAsync(range, token),
            token => readOnlyDatabaseQueries.GetSummaryAsync(range, token),
            cancellationToken);

    public ValueTask<PagedUsageResult<ProjectUsage>> GetProjectsAsync(PagedUsageQuery query, CancellationToken cancellationToken) =>
        QueryAsync(
            token => agentQueries.GetProjectsAsync(query, token),
            token => readOnlyDatabaseQueries.GetProjectsAsync(query, token),
            cancellationToken);

    public ValueTask<PagedUsageResult<ModelUsage>> GetModelsAsync(PagedUsageQuery query, CancellationToken cancellationToken) =>
        QueryAsync(
            token => agentQueries.GetModelsAsync(query, token),
            token => readOnlyDatabaseQueries.GetModelsAsync(query, token),
            cancellationToken);

    public ValueTask<PagedUsageResult<SessionUsage>> GetSessionsAsync(PagedUsageQuery query, CancellationToken cancellationToken) =>
        QueryAsync(
            token => agentQueries.GetSessionsAsync(query, token),
            token => readOnlyDatabaseQueries.GetSessionsAsync(query, token),
            cancellationToken);

    public async ValueTask<int> ExecuteMutationAsync(
        Func<CancellationToken, ValueTask> agentMutation,
        CancellationToken cancellationToken)
    {
        try
        {
            await agentMutation(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (IsAgentUnavailable(exception, cancellationToken))
        {
            return 5;
        }
    }

    private static async ValueTask<T> QueryAsync<T>(
        Func<CancellationToken, ValueTask<T>> agentQuery,
        Func<CancellationToken, ValueTask<T>> databaseQuery,
        CancellationToken cancellationToken)
    {
        try
        {
            return await agentQuery(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsAgentUnavailable(exception, cancellationToken))
        {
            return await databaseQuery(cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsAgentUnavailable(Exception exception, CancellationToken cancellationToken) =>
        exception is TimeoutException or IOException or UnauthorizedAccessException
        || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
}
