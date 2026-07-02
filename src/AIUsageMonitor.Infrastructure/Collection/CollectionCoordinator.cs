using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading.Channels;
using AIUsageMonitor.Core.Abstractions;
using AIUsageMonitor.Core.Collection;
using AIUsageMonitor.Core.Domain;

namespace AIUsageMonitor.Infrastructure.Collection;

public sealed record ProviderCollectionSource(
    ProviderKind Provider,
    string Root,
    string ProjectId,
    string ParserVersion,
    IUsageRecordParser Parser);

public sealed record ProviderCollectionHealth(Exception? LastError, DateTimeOffset? LastSuccessUtc);

public sealed class CollectionCoordinator : ICollectionControl, IAsyncDisposable
{
    private readonly IReadOnlyList<ProviderCollectionSource> sources;
    private readonly IncrementalFileReader reader;
    private readonly IProviderFileWatcherFactory watcherFactory;
    private readonly SourceDiscoveryService discovery;
    private readonly Channel<CollectionWork> queue;
    private readonly ConcurrentDictionary<string, byte> scheduled = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<ProviderKind, ProviderCollectionHealth> health = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task worker;
    private readonly object stateLock = new();
    private readonly List<IProviderFileWatcher> watchers = [];
    private TaskCompletionSource idle = CompletedSource();
    private int pending;
    private bool resumed;
    private bool disposed;

    public CollectionCoordinator(
        IReadOnlyList<ProviderCollectionSource> sources,
        IncrementalFileReader reader,
        IProviderFileWatcherFactory watcherFactory,
        int boundedCapacity = 1024,
        SourceDiscoveryService? discovery = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(watcherFactory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(boundedCapacity);
        this.sources = sources;
        this.reader = reader;
        this.watcherFactory = watcherFactory;
        this.discovery = discovery ?? new SourceDiscoveryService();
        foreach (var source in sources) health[source.Provider] = new ProviderCollectionHealth(null, null);
        ProviderHealth = new ReadOnlyDictionary<ProviderKind, ProviderCollectionHealth>(health);
        queue = Channel.CreateBounded<CollectionWork>(new BoundedChannelOptions(boundedCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        worker = ProcessQueueAsync();
    }

    public IReadOnlyDictionary<ProviderKind, ProviderCollectionHealth> ProviderHealth { get; }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        foreach (var source in sources)
        {
            await ReconcileAsync(source, cancellationToken);
        }
        await DrainAsync(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<IProviderFileWatcher> current;
        lock (stateLock)
        {
            if (!resumed) return;
            resumed = false;
            current = [.. watchers];
            watchers.Clear();
        }
        foreach (var watcher in current) await watcher.DisposeAsync();
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        lock (stateLock) if (resumed) return;
        await RefreshAsync(cancellationToken);
        var created = new List<IProviderFileWatcher>();
        try
        {
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var watcher = watcherFactory.Create(
                    source.Root,
                    path => EnqueueAsync(source, path, lifetime.Token),
                    () => ReconcileAsync(source, lifetime.Token));
                watcher.Start();
                created.Add(watcher);
            }
            lock (stateLock)
            {
                if (resumed) return;
                watchers.AddRange(created);
                resumed = true;
            }
        }
        catch
        {
            foreach (var watcher in created) await watcher.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DrainAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task wait;
        lock (stateLock) wait = pending == 0 ? Task.CompletedTask : idle.Task;
        try
        {
            if (timeout == Timeout.InfiniteTimeSpan) await wait.WaitAsync(cancellationToken);
            else await wait.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException) { throw; }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await PauseAsync(CancellationToken.None);
        disposed = true;
        queue.Writer.TryComplete();
        await worker;
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private async ValueTask ReconcileAsync(ProviderCollectionSource source, CancellationToken cancellationToken)
    {
        foreach (var path in discovery.Discover(source.Root))
        {
            await EnqueueAsync(source, path, cancellationToken);
        }
    }

    private async ValueTask EnqueueAsync(ProviderCollectionSource source, string path, CancellationToken cancellationToken)
    {
        var canonicalPath = Path.GetFullPath(path);
        if (!scheduled.TryAdd(canonicalPath, 0)) return;
        lock (stateLock)
        {
            if (pending++ == 0) idle = NewSource();
        }
        try { await queue.Writer.WriteAsync(new CollectionWork(source, canonicalPath), cancellationToken); }
        catch
        {
            Complete(canonicalPath);
            throw;
        }
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var work in queue.Reader.ReadAllAsync())
        {
            try
            {
                await reader.ReadAsync(work.Path, work.Source.ProjectId, work.Source.ParserVersion, work.Source.Parser, lifetime.Token);
                health[work.Source.Provider] = new ProviderCollectionHealth(null, DateTimeOffset.UtcNow);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                health[work.Source.Provider] = new ProviderCollectionHealth(error, health[work.Source.Provider].LastSuccessUtc);
            }
            finally { Complete(work.Path); }
        }
    }

    private void Complete(string path)
    {
        scheduled.TryRemove(path, out _);
        lock (stateLock) if (--pending == 0) idle.TrySetResult();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CompletedSource() { var source = NewSource(); source.SetResult(); return source; }
    private sealed record CollectionWork(ProviderCollectionSource Source, string Path);
}
