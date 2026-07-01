using AIUsageMonitor.Core.Domain;

namespace AIUsageMonitor.Core.Abstractions;

public interface IUsageEventStore
{
    ValueTask<int> UpsertBatchAsync(
        IReadOnlyList<UsageEvent> events,
        SourceCheckpoint checkpoint,
        CancellationToken cancellationToken);
}
