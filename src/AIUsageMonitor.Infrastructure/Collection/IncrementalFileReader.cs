using AIUsageMonitor.Core.Abstractions;
using AIUsageMonitor.Core.Domain;

namespace AIUsageMonitor.Infrastructure.Collection;

public sealed class IncrementalFileReader
{
    private readonly IUsageEventStore eventStore;
    private readonly ISourceCheckpointStore checkpointStore;

    public IncrementalFileReader(
        IUsageEventStore eventStore,
        ISourceCheckpointStore checkpointStore)
    {
        this.eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        this.checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
    }

    public async ValueTask<int> ReadAsync(
        string path,
        string projectId,
        string parserVersion,
        IUsageRecordParser parser,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parserVersion);
        ArgumentNullException.ThrowIfNull(parser);

        var canonicalPath = Path.GetFullPath(path);
        var file = new FileInfo(canonicalPath);
        if (!file.Exists)
        {
            return 0;
        }

        var checkpoint = await checkpointStore.GetAsync(canonicalPath, cancellationToken);
        var mustReset = checkpoint is not null
            && (file.Length < checkpoint.ByteOffset
                || !string.Equals(
                    checkpoint.ParserVersion,
                    parserVersion,
                    StringComparison.Ordinal));
        var startOffset = checkpoint is null || mustReset ? 0 : checkpoint.ByteOffset;
        var providerState = checkpoint is null || mustReset ? null : checkpoint.ProviderState;

        await using var stream = new FileStream(
            canonicalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var result = await parser.ParseAsync(
            stream,
            new ParseContext(
                canonicalPath,
                projectId,
                startOffset,
                parserVersion,
                providerState),
            cancellationToken);

        file.Refresh();
        var nextCheckpoint = new SourceCheckpoint(
            canonicalPath,
            result.CompleteByteOffset,
            file.Exists ? file.Length : result.CompleteByteOffset,
            file.Exists ? file.LastWriteTimeUtc : DateTimeOffset.UtcNow,
            parserVersion,
            result.NextProviderState);

        return await eventStore.UpsertBatchAsync(
            result.Events,
            nextCheckpoint,
            cancellationToken);
    }
}
