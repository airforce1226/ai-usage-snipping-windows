using System.Threading.Channels;

namespace AIUsageMonitor.Infrastructure.Collection;

public interface IProviderFileWatcher : IAsyncDisposable { void Start(); }

public interface IProviderFileWatcherFactory
{
    IProviderFileWatcher Create(string root, Func<string, ValueTask> onPath, Func<ValueTask> onError);
}

public sealed class ProviderFileWatcherFactory : IProviderFileWatcherFactory
{
    public IProviderFileWatcher Create(string root, Func<string, ValueTask> onPath, Func<ValueTask> onError) =>
        new ProviderFileWatcher(root, onPath, onError);
}

public sealed class ProviderFileWatcher : IProviderFileWatcher
{
    private readonly FileSystemWatcher watcher;
    private readonly Func<string, ValueTask> onPath;
    private readonly Func<ValueTask> onError;
    private readonly Channel<WatcherSignal> events;
    private readonly Task eventPump;
    private int reconciliationRequested;
    private bool disposed;

    public ProviderFileWatcher(string root, Func<string, ValueTask> onPath, Func<ValueTask> onError)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.onPath = onPath ?? throw new ArgumentNullException(nameof(onPath));
        this.onError = onError ?? throw new ArgumentNullException(nameof(onError));
        Directory.CreateDirectory(root);
        events = Channel.CreateBounded<WatcherSignal>(new BoundedChannelOptions(1024)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        watcher = new FileSystemWatcher(Path.GetFullPath(root), "*.jsonl")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Created += HandlePath;
        watcher.Changed += HandlePath;
        watcher.Renamed += HandleRenamed;
        watcher.Error += HandleError;
        eventPump = PumpAsync();
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        watcher.EnableRaisingEvents = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
        events.Writer.TryComplete();
        await eventPump;
    }

    private void HandlePath(object sender, FileSystemEventArgs args) => Enqueue(args.FullPath);
    private void HandleRenamed(object sender, RenamedEventArgs args) => Enqueue(args.FullPath);
    private void HandleError(object sender, ErrorEventArgs args)
    {
        Interlocked.Exchange(ref reconciliationRequested, 1);
        events.Writer.TryWrite(new WatcherSignal(null));
    }

    private void Enqueue(string path)
    {
        try
        {
            if (!events.Writer.TryWrite(new WatcherSignal(Path.GetFullPath(path))))
                Interlocked.Exchange(ref reconciliationRequested, 1);
        }
        catch { Interlocked.Exchange(ref reconciliationRequested, 1); }
    }

    private async Task PumpAsync()
    {
        await foreach (var signal in events.Reader.ReadAllAsync())
        {
            if (signal.Path is not null)
            {
                try { await onPath(signal.Path); }
                catch (OperationCanceledException) { }
                catch { Interlocked.Exchange(ref reconciliationRequested, 1); }
            }
            await ReconcileIfRequestedAsync();
        }
        await ReconcileIfRequestedAsync();
    }

    private async ValueTask ReconcileIfRequestedAsync()
    {
        if (Interlocked.Exchange(ref reconciliationRequested, 0) == 0) return;
        try { await onError(); }
        catch (OperationCanceledException) { }
        catch { }
    }

    private sealed record WatcherSignal(string? Path);
}
