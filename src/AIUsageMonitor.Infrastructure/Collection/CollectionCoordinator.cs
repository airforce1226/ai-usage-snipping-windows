using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading.Channels;
using AIUsageMonitor.Core.Abstractions;
using AIUsageMonitor.Core.Collection;
using AIUsageMonitor.Core.Domain;

namespace AIUsageMonitor.Infrastructure.Collection;

public sealed record ProviderCollectionSource(ProviderKind Provider, string Root, string ProjectId, string ParserVersion, IUsageRecordParser Parser);
public sealed record ProviderCollectionHealth(Exception? LastError, DateTimeOffset? LastSuccessUtc);

public sealed class CollectionCoordinator : ICollectionControl, IAsyncDisposable
{
    private readonly IReadOnlyList<SourceRegistration> sources;
    private readonly IncrementalFileReader reader;
    private readonly IProviderFileWatcherFactory watcherFactory;
    private readonly SourceDiscoveryService discovery;
    private readonly Channel<WorkKey> queue;
    private readonly SemaphoreSlim ingressSlots;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly Dictionary<WorkKey, WorkState> work = [];
    private readonly ConcurrentDictionary<ProviderKind, ProviderCollectionHealth> health = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly object stateLock = new();
    private readonly List<IProviderFileWatcher> watchers = [];
    private readonly Task worker;
    private TaskCompletionSource idle = CompletedSource();
    private int pending;
    private bool resumed;
    private bool disposed;

    public CollectionCoordinator(IReadOnlyList<ProviderCollectionSource> sources, IncrementalFileReader reader,
        IProviderFileWatcherFactory watcherFactory, int boundedCapacity = 1024, SourceDiscoveryService? discovery = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(watcherFactory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(boundedCapacity);
        this.sources = sources.Select((source, index) => new SourceRegistration(index, source)).ToArray();
        this.reader = reader;
        this.watcherFactory = watcherFactory;
        this.discovery = discovery ?? new SourceDiscoveryService();
        foreach (var source in sources) health[source.Provider] = new(null, null);
        ProviderHealth = new ReadOnlyDictionary<ProviderKind, ProviderCollectionHealth>(health);
        ingressSlots = new SemaphoreSlim(boundedCapacity, boundedCapacity);
        queue = Channel.CreateBounded<WorkKey>(new BoundedChannelOptions(boundedCapacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        worker = ProcessQueueAsync();
    }

    public IReadOnlyDictionary<ProviderKind, ProviderCollectionHealth> ProviderHealth { get; }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        foreach (var source in sources) await ReconcileSafelyAsync(source, cancellationToken);
        await DrainAsync(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken)
    {
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (!resumed) return;
            resumed = false;
            var current = watchers.ToArray();
            watchers.Clear();
            foreach (var watcher in current) await watcher.DisposeAsync();
        }
        finally { lifecycleGate.Release(); }
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await lifecycleGate.WaitAsync(cancellationToken);
        var created = new List<IProviderFileWatcher>();
        try
        {
            if (resumed) return;
            foreach (var source in sources)
            {
                created.Add(watcherFactory.Create(source.Source.Root,
                    path => EnqueueAsync(source, path, lifetime.Token),
                    () => ReconcileSafelyAsync(source, lifetime.Token)));
            }
            foreach (var source in sources) await ReconcileSafelyAsync(source, cancellationToken);
            await DrainAsync(Timeout.InfiniteTimeSpan, cancellationToken);
            foreach (var watcher in created) watcher.Start();
            foreach (var source in sources) await ReconcileSafelyAsync(source, cancellationToken);
            await DrainAsync(Timeout.InfiniteTimeSpan, cancellationToken);
            watchers.AddRange(created);
            resumed = true;
        }
        catch
        {
            foreach (var watcher in created) await watcher.DisposeAsync();
            throw;
        }
        finally { lifecycleGate.Release(); }
    }

    public async ValueTask DrainAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task wait;
        lock (stateLock) wait = pending == 0 ? Task.CompletedTask : idle.Task;
        if (timeout == Timeout.InfiniteTimeSpan) await wait.WaitAsync(cancellationToken);
        else await wait.WaitAsync(timeout, cancellationToken);
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
        lifecycleGate.Dispose();
        ingressSlots.Dispose();
    }

    private async ValueTask ReconcileSafelyAsync(SourceRegistration source, CancellationToken token)
    {
        try
        {
            foreach (var path in discovery.Discover(source.Source.Root)) await EnqueueAsync(source, path, token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            health[source.Source.Provider] = new(error, health[source.Source.Provider].LastSuccessUtc);
        }
    }

    private async ValueTask EnqueueAsync(SourceRegistration source, string path, CancellationToken token)
    {
        var key = new WorkKey(source.Id, Path.GetFullPath(path));
        lock (stateLock)
        {
            if (work.TryGetValue(key, out var existing)) { existing.Dirty = true; return; }
        }
        await ingressSlots.WaitAsync(token);
        lock (stateLock)
        {
            if (work.TryGetValue(key, out var existing))
            {
                existing.Dirty = true;
                ingressSlots.Release();
                return;
            }
            work.Add(key, new WorkState(source));
            if (pending++ == 0) idle = NewSource();
        }
        try { await queue.Writer.WriteAsync(key, token); }
        catch { Finish(key); throw; }
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var key in queue.Reader.ReadAllAsync())
        {
            WorkState state;
            lock (stateLock) state = work[key];
            try
            {
                var source = state.Source.Source;
                await reader.ReadAsync(key.Path, source.ProjectId, source.ParserVersion, source.Parser, lifetime.Token);
                health[source.Provider] = new(null, DateTimeOffset.UtcNow);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                var provider = state.Source.Source.Provider;
                health[provider] = new(error, health[provider].LastSuccessUtc);
            }
            bool rerun;
            lock (stateLock)
            {
                rerun = state.Dirty;
                state.Dirty = false;
                if (!rerun)
                {
                    work.Remove(key);
                    if (--pending == 0) idle.TrySetResult();
                }
            }
            if (rerun) await queue.Writer.WriteAsync(key);
            else ingressSlots.Release();
        }
    }

    private void Finish(WorkKey key)
    {
        lock (stateLock)
        {
            work.Remove(key);
            if (--pending == 0) idle.TrySetResult();
        }
        ingressSlots.Release();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CompletedSource() { var result = NewSource(); result.SetResult(); return result; }
    private sealed record SourceRegistration(int Id, ProviderCollectionSource Source);
    private sealed record WorkKey(int SourceId, string Path)
    {
        public bool Equals(WorkKey? other) => other is not null && SourceId == other.SourceId && StringComparer.OrdinalIgnoreCase.Equals(Path, other.Path);
        public override int GetHashCode() => HashCode.Combine(SourceId, StringComparer.OrdinalIgnoreCase.GetHashCode(Path));
    }
    private sealed class WorkState(SourceRegistration source) { public SourceRegistration Source { get; } = source; public bool Dirty { get; set; } }
}
