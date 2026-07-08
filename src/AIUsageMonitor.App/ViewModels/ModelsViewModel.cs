using AIUsageMonitor.Core.Queries;

namespace AIUsageMonitor.App.ViewModels;

public sealed class ModelsViewModel(IUsageQueryService queries, Func<UsageQueryRange> rangeFactory)
    : PagedUsageViewModel<ModelUsage>(rangeFactory)
{
    protected override ValueTask<PagedUsageResult<ModelUsage>> QueryAsync(PagedUsageQuery query, CancellationToken cancellationToken) =>
        queries.GetModelsAsync(query, cancellationToken);
}
