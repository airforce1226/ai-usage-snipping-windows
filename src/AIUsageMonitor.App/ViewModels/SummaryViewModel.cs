using AIUsageMonitor.Core.Queries;

namespace AIUsageMonitor.App.ViewModels;

public sealed class SummaryViewModel(IUsageQueryService queries, Func<UsageQueryRange> rangeFactory) : UsagePageViewModel
{
    private long inputTokens;
    private long outputTokens;
    private long cacheReadTokens;
    private long cacheWriteTokens;
    private long eventCount;

    public long InputTokens { get => inputTokens; private set => SetProperty(ref inputTokens, value); }
    public long OutputTokens { get => outputTokens; private set => SetProperty(ref outputTokens, value); }
    public long CacheReadTokens { get => cacheReadTokens; private set => SetProperty(ref cacheReadTokens, value); }
    public long CacheWriteTokens { get => cacheWriteTokens; private set => SetProperty(ref cacheWriteTokens, value); }
    public long EventCount { get => eventCount; private set => SetProperty(ref eventCount, value); }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        UsageSummary value = await queries.GetSummaryAsync(rangeFactory(), cancellationToken).ConfigureAwait(false);
        InputTokens = value.InputTokens;
        OutputTokens = value.OutputTokens;
        CacheReadTokens = value.CacheReadTokens;
        CacheWriteTokens = value.CacheWriteTokens;
        EventCount = value.EventCount;
        DataUpdatedAt = value.DatabaseUpdatedAtUtc;
        IsEmpty = value.EventCount == 0;
    }
}
