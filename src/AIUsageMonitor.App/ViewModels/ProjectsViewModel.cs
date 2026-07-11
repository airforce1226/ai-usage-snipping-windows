using AIUsageMonitor.Core.Queries;

namespace AIUsageMonitor.App.ViewModels;

public sealed class ProjectsViewModel(IUsageQueryService queries, Func<UsageQueryRange> rangeFactory)
    : PagedUsageViewModel<ProjectUsage>(rangeFactory)
{
    protected override ValueTask<PagedUsageResult<ProjectUsage>> QueryAsync(PagedUsageQuery query, CancellationToken cancellationToken) =>
        queries.GetProjectsAsync(query, cancellationToken);
}
