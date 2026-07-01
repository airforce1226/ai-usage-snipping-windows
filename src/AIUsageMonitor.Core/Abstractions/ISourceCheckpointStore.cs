using AIUsageMonitor.Core.Domain;

namespace AIUsageMonitor.Core.Abstractions;

public interface ISourceCheckpointStore
{
    ValueTask<SourceCheckpoint?> GetAsync(
        string canonicalPath,
        CancellationToken cancellationToken);
}
