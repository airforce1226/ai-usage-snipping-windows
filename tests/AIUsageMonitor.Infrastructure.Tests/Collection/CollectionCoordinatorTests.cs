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
        var claudeRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "claude")).FullName;
        var codexRoot = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "codex")).FullName;
        await File.WriteAllTextAsync(Path.Combine(claudeRoot, "bad.jsonl"), "anything\n");
        await WriteClaudeAsync(Path.Combine(codexRoot, "good.jsonl"), "good");
        var store = new RecordingStore();
        await using var coordinator = CreateCoordinator(
            store,
            new FakeWatcherFactory(),
            new ProviderCollectionSource(ProviderKind.Claude, claudeRoot, "project", "bad-v1", new ThrowingParser()),
            Source(ProviderKind.Codex, codexRoot));

        await coordinator.RefreshAsync(CancellationToken.None);

        Assert.Equal("good", Assert.Single(store.Events).MessageId);
        Assert.NotNull(coordinator.ProviderHealth[ProviderKind.Claude].LastError);
        Assert.Null(coordinator.ProviderHealth[ProviderKind.Codex].LastError);
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

    private sealed class FakeWatcherFactory(Action? onStart = null) : IProviderFileWatcherFactory
    {
        public List<FakeWatcher> Created { get; } = [];
        public IProviderFileWatcher Create(string root, Func<string, ValueTask> onPath, Func<ValueTask> onError)
        {
            var watcher = new FakeWatcher(onPath, onStart);
            Created.Add(watcher);
            return watcher;
        }
    }

    private sealed class FakeWatcher(Func<string, ValueTask> onPath, Action? onStart) : IProviderFileWatcher
    {
        public bool IsStarted { get; private set; }
        public bool IsDisposed { get; private set; }
        public int DisposeCount { get; private set; }
        public void Start() { onStart?.Invoke(); IsStarted = true; }
        public ValueTask RaisePathAsync(string path) => onPath(path);
        public ValueTask DisposeAsync() { DisposeCount++; IsDisposed = true; return ValueTask.CompletedTask; }
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
}
