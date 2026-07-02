namespace AIUsageMonitor.Infrastructure.Collection;

public interface IProviderFileWatcher : IAsyncDisposable
{
    void Start();
}

public interface IProviderFileWatcherFactory
{
    IProviderFileWatcher Create(
        string root,
        Func<string, ValueTask> onPath,
        Func<ValueTask> onError);
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
    private bool disposed;

    public ProviderFileWatcher(string root, Func<string, ValueTask> onPath, Func<ValueTask> onError)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.onPath = onPath ?? throw new ArgumentNullException(nameof(onPath));
        this.onError = onError ?? throw new ArgumentNullException(nameof(onError));
        Directory.CreateDirectory(root);
        watcher = new FileSystemWatcher(Path.GetFullPath(root), "*.jsonl")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Created += HandlePath;
        watcher.Changed += HandlePath;
        watcher.Renamed += HandleRenamed;
        watcher.Error += HandleError;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        watcher.EnableRaisingEvents = true;
    }

    public ValueTask DisposeAsync()
    {
        if (disposed) return ValueTask.CompletedTask;
        disposed = true;
        watcher.Dispose();
        return ValueTask.CompletedTask;
    }

    private void HandlePath(object sender, FileSystemEventArgs args) => QueuePath(args.FullPath);
    private void HandleRenamed(object sender, RenamedEventArgs args) => QueuePath(args.FullPath);
    private void HandleError(object sender, ErrorEventArgs args) => _ = InvokeSafelyAsync(onError);
    private void QueuePath(string path) => _ = InvokeSafelyAsync(() => onPath(Path.GetFullPath(path)));

    private static async Task InvokeSafelyAsync(Func<ValueTask> callback)
    {
        try { await callback(); }
        catch (OperationCanceledException) { }
    }
}
