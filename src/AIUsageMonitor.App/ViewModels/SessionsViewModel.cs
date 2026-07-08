using AIUsageMonitor.Core.Queries;

namespace AIUsageMonitor.App.ViewModels;

public sealed class SessionsViewModel(IUsageQueryService queries, Func<UsageQueryRange> rangeFactory)
    : PagedUsageViewModel<SessionUsage>(rangeFactory)
{
    protected override ValueTask<PagedUsageResult<SessionUsage>> QueryAsync(PagedUsageQuery query, CancellationToken cancellationToken) =>
        queries.GetSessionsAsync(query, cancellationToken);
}
