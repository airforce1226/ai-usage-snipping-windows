using AIUsageMonitor.Ipc.Client;

namespace AIUsageMonitor.App.ViewModels;

public enum PageKind { Summary, Projects, Models, Sessions, Settings }

public sealed class MainViewModel(
    IReadOnlyDictionary<PageKind, IUsagePageViewModel> pages,
    ICollectionRefreshClient refreshClient) : ObservableObject, IDisposable
{
    private readonly object refreshLock = new();
    private CancellationTokenSource navigationCancellation = new();
    private CancellationTokenSource lifetimeCancellation = new();
    private IUsagePageViewModel? activePage;
    private PageKind activePageKind;
    private Task? refreshTask;

    public IUsagePageViewModel? ActivePage { get => activePage; private set => SetProperty(ref activePage, value); }
    public PageKind ActivePageKind { get => activePageKind; private set => SetProperty(ref activePageKind, value); }

    public async Task NavigateAsync(PageKind pageKind)
    {
        if (!pages.TryGetValue(pageKind, out IUsagePageViewModel? page)) return;
        navigationCancellation.Cancel();
        navigationCancellation.Dispose();
        navigationCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
        ActivePageKind = pageKind;
        ActivePage = page;
        await page.LoadAsync(navigationCancellation.Token).ConfigureAwait(false);
    }

    public Task RefreshAsync()
    {
        lock (refreshLock)
        {
            return refreshTask ??= RefreshCoreAsync();
        }
    }

    public void Dispose()
    {
        navigationCancellation.Cancel();
        navigationCancellation.Dispose();
        lifetimeCancellation.Cancel();
        lifetimeCancellation.Dispose();
    }

    private async Task RefreshCoreAsync()
    {
        try
        {
            await refreshClient.RefreshAsync(lifetimeCancellation.Token).ConfigureAwait(false);
            if (ActivePage is not null) await ActivePage.LoadAsync(lifetimeCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (refreshLock) refreshTask = null;
        }
    }
}
