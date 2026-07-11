using AIUsageMonitor.Core.Queries;

namespace AIUsageMonitor.App.ViewModels;

public abstract class PagedUsageViewModel<T>(Func<UsageQueryRange> rangeFactory) : UsagePageViewModel
{
    private IReadOnlyList<T> items = [];
    private int offset;
    private long totalCount;

    public IReadOnlyList<T> Items { get => items; private set => SetProperty(ref items, value); }
    public int Offset { get => offset; private set { if (SetProperty(ref offset, value)) { Notify(nameof(CanGoPrevious)); Notify(nameof(CanGoNext)); } } }
    public int Limit { get; } = 50;
    public long TotalCount { get => totalCount; private set { if (SetProperty(ref totalCount, value)) Notify(nameof(CanGoNext)); } }
    public bool CanGoPrevious => Offset > 0;
    public bool CanGoNext => Offset + Limit < TotalCount;

    public async Task NextAsync(CancellationToken cancellationToken)
    {
        if (!CanGoNext) return;
        Offset += Limit;
        await LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PreviousAsync(CancellationToken cancellationToken)
    {
        if (!CanGoPrevious) return;
        Offset = Math.Max(0, Offset - Limit);
        await LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    protected sealed override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        PagedUsageResult<T> value = await QueryAsync(new(rangeFactory(), new(Offset, Limit)), cancellationToken).ConfigureAwait(false);
        Items = value.Items;
        Offset = value.Offset;
        TotalCount = value.TotalCount;
        DataUpdatedAt = value.DatabaseUpdatedAtUtc;
        IsEmpty = value.Items.Count == 0;
    }

    protected abstract ValueTask<PagedUsageResult<T>> QueryAsync(PagedUsageQuery query, CancellationToken cancellationToken);
}
