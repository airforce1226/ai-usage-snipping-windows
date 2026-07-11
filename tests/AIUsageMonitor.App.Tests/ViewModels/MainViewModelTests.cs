using AIUsageMonitor.App.ViewModels;
using AIUsageMonitor.Ipc.Client;

namespace AIUsageMonitor.App.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task NavigateAsync_LoadsOnlySelectedPage()
    {
        var summary = new RecordingPage();
        var projects = new RecordingPage();
        using var viewModel = Create(summary, projects, new BlockingRefresh(false));

        await viewModel.NavigateAsync(PageKind.Projects);

        Assert.Equal(0, summary.LoadCalls);
        Assert.Equal(1, projects.LoadCalls);
        Assert.Same(projects, viewModel.ActivePage);
    }

    [Fact]
    public async Task RefreshAsync_SharesOneInFlightRefreshAndReloadsActivePage()
    {
        var page = new RecordingPage();
        var refresh = new BlockingRefresh(true);
        using var viewModel = Create(page, new RecordingPage(), refresh);
        await viewModel.NavigateAsync(PageKind.Summary);

        Task first = viewModel.RefreshAsync();
        Task second = viewModel.RefreshAsync();
        Assert.Same(first, second);
        refresh.Complete();
        await first;

        Assert.Equal(1, refresh.Calls);
        Assert.Equal(2, page.LoadCalls);
    }

    private static MainViewModel Create(IUsagePageViewModel summary, IUsagePageViewModel projects, ICollectionRefreshClient refresh) =>
        new(new Dictionary<PageKind, IUsagePageViewModel>
        {
            [PageKind.Summary] = summary,
            [PageKind.Projects] = projects,
        }, refresh);

    private sealed class RecordingPage : IUsagePageViewModel
    {
        public int LoadCalls { get; private set; }
        public bool IsLoading => false;
        public bool IsStale => false;
        public bool IsEmpty => false;
        public string? ErrorMessage => null;
        public DateTimeOffset? DataUpdatedAt => DateTimeOffset.UnixEpoch;
        public Task LoadAsync(CancellationToken cancellationToken) { LoadCalls++; return Task.CompletedTask; }
    }

    private sealed class BlockingRefresh(bool block) : ICollectionRefreshClient
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public ValueTask RefreshAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return block ? new ValueTask(completion.Task) : ValueTask.CompletedTask;
        }
        public void Complete() => completion.SetResult();
    }
}
