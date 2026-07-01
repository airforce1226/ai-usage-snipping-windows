using System.Text;
using System.Text.Json;
using AIUsageMonitor.Core.Abstractions;
using AIUsageMonitor.Core.Domain;
using AIUsageMonitor.Infrastructure.Collection;
using AIUsageMonitor.Infrastructure.Providers.Claude;

namespace AIUsageMonitor.Infrastructure.Tests.Collection;

public sealed class IncrementalFileReaderTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"AIUsageMonitor-{Guid.NewGuid():N}");

    public IncrementalFileReaderTests()
    {
        Directory.CreateDirectory(temporaryDirectory);
    }

    [Fact]
    public async Task ReadAsync_LeavesPartialLineForNextAppend()
    {
        var path = Path.Combine(temporaryDirectory, "session.jsonl");
        var firstLine = ClaudeLine("message-1", 10, 2) + "\n";
        var secondLine = ClaudeLine("message-2", 20, 4);
        await File.WriteAllTextAsync(path, firstLine + secondLine[..20], Encoding.UTF8);
        var store = new RecordingStore();
        var reader = new IncrementalFileReader(store, store);

        var firstCount = await reader.ReadAsync(
            path, "alpha", "claude-v1", new ClaudeUsageRecordParser(), CancellationToken.None);

        Assert.Equal(1, firstCount);
        var firstNewlineOffset = Array.IndexOf(await File.ReadAllBytesAsync(path), (byte)'\n') + 1;
        Assert.Equal(firstNewlineOffset, store.Checkpoint!.ByteOffset);

        await File.AppendAllTextAsync(path, secondLine[20..] + "\n", Encoding.UTF8);
        var secondCount = await reader.ReadAsync(
            path, "alpha", "claude-v1", new ClaudeUsageRecordParser(), CancellationToken.None);

        Assert.Equal(1, secondCount);
        Assert.Equal("message-2", store.Events[^1].MessageId);
        Assert.Equal(new FileInfo(path).Length, store.Checkpoint!.ByteOffset);
    }

    [Fact]
    public async Task ReadAsync_ResetsCheckpointWhenFileIsTruncated()
    {
        var path = Path.Combine(temporaryDirectory, "session.jsonl");
        await File.WriteAllTextAsync(path, ClaudeLine("long-message-identifier", 10, 2) + "\n");
        var store = new RecordingStore();
        var reader = new IncrementalFileReader(store, store);
        await reader.ReadAsync(
            path, "alpha", "claude-v1", new ClaudeUsageRecordParser(), CancellationToken.None);

        await File.WriteAllTextAsync(path, ClaudeLine("m2", 3, 1) + "\n");
        var count = await reader.ReadAsync(
            path, "alpha", "claude-v1", new ClaudeUsageRecordParser(), CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Equal("m2", store.Events[^1].MessageId);
        Assert.Equal(new FileInfo(path).Length, store.Checkpoint!.ByteOffset);
    }

    public void Dispose()
    {
        Directory.Delete(temporaryDirectory, recursive: true);
    }

    private static string ClaudeLine(string messageId, long input, long output)
    {
        return JsonSerializer.Serialize(new
        {
            type = "assistant",
            uuid = messageId,
            sessionId = "session-1",
            cwd = "C:\\work\\alpha",
            timestamp = "2026-07-01T01:00:00Z",
            message = new
            {
                model = "claude-sonnet-4-6",
                usage = new
                {
                    input_tokens = input,
                    output_tokens = output,
                },
            },
        });
    }

    private sealed class RecordingStore : IUsageEventStore, ISourceCheckpointStore
    {
        public List<UsageEvent> Events { get; } = [];

        public SourceCheckpoint? Checkpoint { get; private set; }

        public ValueTask<SourceCheckpoint?> GetAsync(
            string canonicalPath,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Checkpoint);
        }

        public ValueTask<int> UpsertBatchAsync(
            IReadOnlyList<UsageEvent> events,
            SourceCheckpoint checkpoint,
            CancellationToken cancellationToken)
        {
            Events.AddRange(events);
            Checkpoint = checkpoint;
            return ValueTask.FromResult(events.Count);
        }
    }
}
