using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using AIUsageMonitor.Core.Abstractions;
using AIUsageMonitor.Core.Domain;
using AIUsageMonitor.Infrastructure.Collection;
using AIUsageMonitor.Infrastructure.Providers.Claude;

namespace AIUsageMonitor.Infrastructure.Tests.Collection;

public sealed class CollectionCoordinatorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"AIUsageMonitor-{Guid.NewGuid():N}");

    public CollectionCoordinatorTests() => Directory.CreateDirectory(temporaryDirectory);

    [Fact]
    public async Task RefreshAsync_RecursivelyDiscoversJsonlAndProcessesEachOnce()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "claude")).FullName;
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        await WriteClaudeAsync(Path.Combine(root, "one.jsonl"), "one");
        await WriteClaudeAsync(Path.Combine(root, "nested", "two.jsonl"), "two");
        await File.WriteAllTextAsync(Path.Combine(root, "ignored.txt"), "ignored");
        var store = new RecordingStore();
        await using var coordinator = CreateCoordinator(store, new FakeWatcherFactory(), Source(ProviderKind.Claude, root));

        await coordinator.RefreshAsync(CancellationToken.None);

        Assert.Equal(2, store.Events.Count);
        Assert.Equal(2, store.Paths.Count);
    }

    [Fact]
    public async Task WatcherEvent_ProcessesChangedFile()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "claude")).FullName;
        var path = Path.Combine(root, "session.jsonl");
        await WriteClaudeAsync(path, "one");
        var store = new RecordingStore();
        var watchers = new FakeWatcherFactory();
        await using var coordinator = CreateCoordinator(store, watchers, Source(ProviderKind.Claude, root));
        await coordinator.ResumeAsync(CancellationToken.None);
        await AppendClaudeAsync(path, "two");

        await watchers.Created.Single().RaisePathAsync(path);
        await coordinator.DrainAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(new[] { "one", "two" }, store.Events.Select(item => item.MessageId));
    }

    [Fact]
    public async Task PauseAsync_IsIdempotentAndDisposesWatchers()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "claude")).FullName;
        var watchers = new FakeWatcherFactory();
        await using var coordinator = CreateCoordinator(new RecordingStore(), watchers, Source(ProviderKind.Claude, root));
        await coordinator.ResumeAsync(CancellationToken.None);

        await coordinator.PauseAsync(CancellationToken.None);
        await coordinator.PauseAsync(CancellationToken.None);

        Assert.True(watchers.Created.Single().IsDisposed);
        Assert.Equal(1, watchers.Created.Single().DisposeCount);
    }

    [Fact]
    public async Task ResumeAsync_CompletesReconciliationBeforeStartingWatchers()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "claude")).FullName;
        await WriteClaudeAsync(Path.Combine(root, "session.jsonl"), "one");
        var store = new RecordingStore();
        var watchers = new FakeWatcherFactory(() => Assert.Single(store.Events));
        await using var coordinator = CreateCoordinator(store, watchers, Source(ProviderKind.Claude, root));

        await coordinator.ResumeAsync(CancellationToken.None);

        Assert.True(watchers.Created.Single().IsStarted);
    }

    [Fact]
    public async Task ProviderFailure_IsCapturedWithoutStoppingOtherProvider()
    {
        var codexRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "codex")).FullName;
        var claudeRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "claude")).FullName;
        await File.WriteAllTextAsync(Path.Combine(codexRoot, "bad.jsonl"), "anything\n");
        await WriteClaudeAsync(Path.Combine(claudeRoot, "good.jsonl"), "good");
        var store = new RecordingStore();
        await using var coordinator = CreateCoordinator(
            store,
            new FakeWatcherFactory(),
            new ProviderCollectionSource(ProviderKind.Codex, codexRoot, "project", "bad-v1", new ThrowingParser()),
            Source(ProviderKind.Claude, claudeRoot));

        await coordinator.RefreshAsync(CancellationToken.None);

        Assert.Equal("good", Assert.Single(store.Events).MessageId);
        Assert.NotNull(coordinator.ProviderHealth[ProviderKind.Codex].LastError);
        Assert.Null(coordinator.ProviderHealth[ProviderKind.Claude].LastError);
    }

    [Fact]
    public async Task DrainAsync_WaitsForInflightWorkAndHonorsTimeoutAndCancellation()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "claude")).FullName;
        var path = Path.Combine(root, "session.jsonl");
        await WriteClaudeAsync(path, "one");
        var parser = new BlockingParser(new ClaudeUsageRecordParser());
        var watchers = new FakeWatcherFactory();
        await using var coordinator = CreateCoordinator(new RecordingStore(), watchers,
            new ProviderCollectionSource(ProviderKind.Claude, root, "project", "v1", parser));
        await coordinator.ResumeAsync(CancellationToken.None);
        parser.BlockNext();
        await watchers.Created.Single().RaisePathAsync(path);
        await parser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<TimeoutException>(() => coordinator.DrainAsync(TimeSpan.Zero, CancellationToken.None).AsTask());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.DrainAsync(TimeSpan.FromSeconds(5), cancellation.Token).AsTask());
        parser.Release();
        await coordinator.DrainAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
    }

    [Fact]
    public async Task RepeatedRefreshAndWatcherEvents_DoNotDuplicateStoredEvents()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "claude")).FullName;
        var path = Path.Combine(root, "session.jsonl");
        await WriteClaudeAsync(path, "one");
        var store = new RecordingStore();
        var watchers = new FakeWatcherFactory();
        await using var coordinator = CreateCoordinator(store, watchers, Source(ProviderKind.Claude, root));
        await coordinator.ResumeAsync(CancellationToken.None);

        await coordinator.RefreshAsync(CancellationToken.None);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => watchers.Created.Single().RaisePathAsync(path).AsTask()));
        await coordinator.DrainAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Single(store.Events);
    }

    [Fact]
    public async Task EventDuringInflightProcessing_RequestsAnotherPass()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "dirty")).FullName;
        var path = Path.Combine(root, "session.jsonl");
        await WriteClaudeAsync(path, "one");
        var parser = new CountingBlockingParser();
        var watchers = new FakeWatcherFactory();
        await using var coordinator = CreateCoordinator(new RecordingStore(), watchers,
            new ProviderCollectionSource(ProviderKind.Claude, root, "project", "v1", parser));
        await coordinator.ResumeAsync(CancellationToken.None);
        var baseline = parser.Calls;
        parser.BlockNext();
        var first = watchers.Created.Single().RaisePathAsync(path).AsTask();
        await parser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await watchers.Created.Single().RaisePathAsync(path);
        parser.Release();
        await first;
        await coordinator.DrainAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Equal(baseline + 2, parser.Calls);
    }

    [Fact]
    public async Task SameCanonicalPath_IsProcessedForEachConfiguredSource()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "shared")).FullName;
        await WriteClaudeAsync(Path.Combine(root, "session.jsonl"), "one");
        var first = new CountingParser();
        var second = new CountingParser();
        await using var coordinator = CreateCoordinator(new RecordingStore(), new FakeWatcherFactory(),
            new ProviderCollectionSource(ProviderKind.Claude, root, "first", "v1", first),
            new ProviderCollectionSource(ProviderKind.Codex, root, "second", "v1", second));
        await coordinator.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public async Task DiscoveryFailure_IsRecordedAndDoesNotStopOtherSource()
    {
        var badRoot = Path.Combine(temporaryDirectory, "bad");
        var goodRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "good")).FullName;
        await WriteClaudeAsync(Path.Combine(goodRoot, "session.jsonl"), "good");
        var store = new RecordingStore();
        await using var coordinator = new CollectionCoordinator(
            [Source(ProviderKind.Codex, badRoot), Source(ProviderKind.Claude, goodRoot)],
            new IncrementalFileReader(store, store), new FakeWatcherFactory(),
            discovery: new ThrowingDiscovery(badRoot));
        await coordinator.RefreshAsync(CancellationToken.None);
        Assert.Single(store.Events);
        Assert.IsType<IOException>(coordinator.ProviderHealth[ProviderKind.Codex].LastError);
        Assert.Null(coordinator.ProviderHealth[ProviderKind.Claude].LastError);
    }

    [Fact]
    public async Task WatcherError_ReconcilesProvider()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "error")).FullName;
        var store = new RecordingStore();
        var watchers = new FakeWatcherFactory();
        await using var coordinator = CreateCoordinator(store, watchers, Source(ProviderKind.Claude, root));
        await coordinator.ResumeAsync(CancellationToken.None);
        await WriteClaudeAsync(Path.Combine(root, "late.jsonl"), "late");
        await watchers.Created.Single().RaiseErrorAsync();
        await coordinator.DrainAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Equal("late", Assert.Single(store.Events).MessageId);
    }

    [Fact]
    public async Task BoundedIngress_WaitsWhenUniquePathCapacityIsFull()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "pressure")).FullName;
        var firstPath = Path.Combine(root, "first.jsonl");
        var secondPath = Path.Combine(root, "second.jsonl");
        await WriteClaudeAsync(firstPath, "first");
        await WriteClaudeAsync(secondPath, "second");
        var parser = new CountingBlockingParser();
        var watchers = new FakeWatcherFactory();
        await using var coordinator = new CollectionCoordinator(
            [new ProviderCollectionSource(ProviderKind.Claude, root, "project", "v1", parser)],
            new IncrementalFileReader(new RecordingStore(), new RecordingStore()), watchers, boundedCapacity: 1);
        await coordinator.ResumeAsync(CancellationToken.None);
        parser.BlockNext();
        var first = watchers.Created.Single().RaisePathAsync(firstPath).AsTask();
        await parser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = watchers.Created.Single().RaisePathAsync(secondPath).AsTask();
        Assert.False(second.IsCompleted);
        parser.Release();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.DrainAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
    }

    [Fact]
    public async Task Resume_FinalReconciliationClosesWatcherStartGap()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "gap")).FullName;
        var path = Path.Combine(root, "during-start.jsonl");
        var store = new RecordingStore();
        var watchers = new FakeWatcherFactory(() => File.WriteAllText(path, ClaudeLine("gap") + "\n"));
        await using var coordinator = CreateCoordinator(store, watchers, Source(ProviderKind.Claude, root));
        await coordinator.ResumeAsync(CancellationToken.None);
        Assert.Equal("gap", Assert.Single(store.Events).MessageId);
    }

    [Fact]
    public async Task PauseAndResume_AreSerializedUntilWatcherDisposalCompletes()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "lifecycle")).FullName;
        var disposeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watchers = new FakeWatcherFactory(onDispose: async () =>
        {
            disposeEntered.TrySetResult();
            await releaseDispose.Task;
        });
        await using var coordinator = CreateCoordinator(new RecordingStore(), watchers, Source(ProviderKind.Claude, root));
        await coordinator.ResumeAsync(CancellationToken.None);
        var pause = coordinator.PauseAsync(CancellationToken.None).AsTask();
        await disposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var resume = coordinator.ResumeAsync(CancellationToken.None).AsTask();
        Assert.Single(watchers.Created);
        releaseDispose.TrySetResult();
        await Task.WhenAll(pause, resume).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, watchers.Created.Count);
    }

    [Fact]
    public async Task DrainAsync_WaitsForEnqueueAlreadyBlockedOnIngressCapacity()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "drain-reservation")).FullName;
        var firstPath = Path.Combine(root, "first.jsonl");
        var secondPath = Path.Combine(root, "second.jsonl");
        await WriteClaudeAsync(firstPath, "first");
        await WriteClaudeAsync(secondPath, "second");
        var parser = new SequencedBlockingParser();
        var watchers = new FakeWatcherFactory();
        await using var coordinator = new CollectionCoordinator(
            [new ProviderCollectionSource(ProviderKind.Claude, root, "project", "v1", parser)],
            new IncrementalFileReader(new RecordingStore(), new RecordingStore()), watchers, boundedCapacity: 1);
        await coordinator.ResumeAsync(CancellationToken.None);
        var firstGate = parser.BlockNext();
        var secondGate = parser.BlockNext();
        var first = watchers.Created.Single().RaisePathAsync(firstPath).AsTask();
        await firstGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = watchers.Created.Single().RaisePathAsync(secondPath).AsTask();
        var drain = coordinator.DrainAsync(TimeSpan.FromSeconds(5), CancellationToken.None).AsTask();
        firstGate.Release.TrySetResult();
        await secondGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var completedBeforeWaitingEnqueueFinished = drain.IsCompleted;
        secondGate.Release.TrySetResult();
        await Task.WhenAll(first, second, drain).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(completedBeforeWaitingEnqueueFinished);
    }

    [Fact]
    public async Task DisposeAsync_WinsAgainstConcurrentResumeAndLeavesNoWatchers()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "dispose-race")).FullName;
        var disposeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watchers = new FakeWatcherFactory(onDispose: async () =>
        {
            disposeEntered.TrySetResult();
            await releaseDispose.Task;
        });
        var coordinator = CreateCoordinator(new RecordingStore(), watchers, Source(ProviderKind.Claude, root));
        await coordinator.ResumeAsync(CancellationToken.None);
        var disposal = coordinator.DisposeAsync().AsTask();
        await disposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var resume = coordinator.ResumeAsync(CancellationToken.None).AsTask();
        releaseDispose.TrySetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => resume);
        Assert.Single(watchers.Created);
        Assert.True(watchers.Created.Single().IsDisposed);
    }

    public void Dispose() => Directory.Delete(temporaryDirectory, recursive: true);

    private static ProviderCollectionSource Source(ProviderKind provider, string root) =>
        new(provider, root, "project", "v1", new ClaudeUsageRecordParser());

    private static CollectionCoordinator CreateCoordinator(RecordingStore store, FakeWatcherFactory watchers, params ProviderCollectionSource[] sources) =>
        new(sources, new IncrementalFileReader(store, store), watchers, boundedCapacity: 1);

    private static async Task WriteClaudeAsync(string path, string messageId) =>
        await File.WriteAllTextAsync(path, ClaudeLine(messageId) + "\n", Encoding.UTF8);

    private static async Task AppendClaudeAsync(string path, string messageId) =>
        await File.AppendAllTextAsync(path, ClaudeLine(messageId) + "\n", Encoding.UTF8);

    private static string ClaudeLine(string messageId) => JsonSerializer.Serialize(new
    {
        type = "assistant", uuid = messageId, sessionId = "session", cwd = "C:\\work",
        timestamp = "2026-07-01T01:00:00Z",
        message = new { model = "claude", usage = new { input_tokens = 1, output_tokens = 1 } },
    });

    private sealed class RecordingStore : IUsageEventStore, ISourceCheckpointStore
    {
        private readonly ConcurrentDictionary<string, SourceCheckpoint> checkpoints = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, UsageEvent> events = new(StringComparer.Ordinal);
        public IReadOnlyCollection<UsageEvent> Events => events.Values.ToArray();
        public IReadOnlyCollection<string> Paths => checkpoints.Keys.ToArray();
        public ValueTask<SourceCheckpoint?> GetAsync(string path, CancellationToken token) =>
            ValueTask.FromResult(checkpoints.TryGetValue(path, out var checkpoint) ? checkpoint : null);
        public ValueTask<int> UpsertBatchAsync(IReadOnlyList<UsageEvent> items, SourceCheckpoint checkpoint, CancellationToken token)
        {
            checkpoints[checkpoint.CanonicalPath] = checkpoint;
            var count = 0;
            foreach (var item in items) if (events.TryAdd(item.StableKey, item)) count++;
            return ValueTask.FromResult(count);
        }
    }

    private sealed class FakeWatcherFactory(Action? onStart = null, Func<ValueTask>? onDispose = null) : IProviderFileWatcherFactory
    {
        public List<FakeWatcher> Created { get; } = [];
        public IProviderFileWatcher Create(string root, Func<string, ValueTask> onPath, Func<ValueTask> onError)
        {
            var watcher = new FakeWatcher(onPath, onError, onStart, onDispose);
            Created.Add(watcher);
            return watcher;
        }
    }

    private sealed class FakeWatcher(Func<string, ValueTask> onPath, Func<ValueTask> onError, Action? onStart, Func<ValueTask>? onDispose) : IProviderFileWatcher
    {
        public bool IsStarted { get; private set; }
        public bool IsDisposed { get; private set; }
        public int DisposeCount { get; private set; }
        public void Start() { onStart?.Invoke(); IsStarted = true; }
        public ValueTask RaisePathAsync(string path) => onPath(path);
        public ValueTask RaiseErrorAsync() => onError();
        public async ValueTask DisposeAsync() { DisposeCount++; IsDisposed = true; if (onDispose is not null) await onDispose(); }
    }

    private sealed class ThrowingParser : IUsageRecordParser
    {
        public ValueTask<ParseResult> ParseAsync(Stream stream, ParseContext context, CancellationToken token) => throw new InvalidDataException("bad provider");
    }

    private sealed class BlockingParser(IUsageRecordParser inner) : IUsageRecordParser
    {
        private TaskCompletionSource entered = NewSource();
        private TaskCompletionSource release = NewSource();
        private bool block;
        public TaskCompletionSource Entered => entered;
        public void BlockNext() { block = true; entered = NewSource(); release = NewSource(); }
        public void Release() => release.TrySetResult();
        public async ValueTask<ParseResult> ParseAsync(Stream stream, ParseContext context, CancellationToken token)
        {
            if (block) { block = false; entered.TrySetResult(); await release.Task.WaitAsync(token); }
            return await inner.ParseAsync(stream, context, token);
        }
        private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private class CountingParser : IUsageRecordParser
    {
        public int Calls { get; protected set; }
        public virtual ValueTask<ParseResult> ParseAsync(Stream stream, ParseContext context, CancellationToken token)
        {
            Calls++;
            return ValueTask.FromResult(new ParseResult([], [], stream.Length, new Dictionary<string, string>()));
        }
    }

    private sealed class CountingBlockingParser : CountingParser
    {
        private TaskCompletionSource entered = NewSource();
        private TaskCompletionSource release = NewSource();
        private bool block;
        public TaskCompletionSource Entered => entered;
        public void BlockNext() { block = true; entered = NewSource(); release = NewSource(); }
        public void Release() => release.TrySetResult();
        public override async ValueTask<ParseResult> ParseAsync(Stream stream, ParseContext context, CancellationToken token)
        {
            Calls++;
            if (block) { block = false; entered.TrySetResult(); await release.Task.WaitAsync(token); }
            return new ParseResult([], [], stream.Length, new Dictionary<string, string>());
        }
        private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ThrowingDiscovery(string badRoot) : SourceDiscoveryService
    {
        public override IEnumerable<string> Discover(string root) =>
            root == badRoot ? throw new IOException("discovery failed") : base.Discover(root);
    }

    private sealed class SequencedBlockingParser : IUsageRecordParser
    {
        private readonly ConcurrentQueue<Gate> gates = new();
        public Gate BlockNext() { var gate = new Gate(); gates.Enqueue(gate); return gate; }
        public async ValueTask<ParseResult> ParseAsync(Stream stream, ParseContext context, CancellationToken token)
        {
            if (gates.TryDequeue(out var gate))
            {
                gate.Entered.TrySetResult();
                await gate.Release.Task.WaitAsync(token);
            }
            return new ParseResult([], [], stream.Length, new Dictionary<string, string>());
        }
    }

    private sealed class Gate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
