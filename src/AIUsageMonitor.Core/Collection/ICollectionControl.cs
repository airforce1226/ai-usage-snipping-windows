namespace AIUsageMonitor.Core.Collection;

public interface ICollectionControl
{
    ValueTask RefreshAsync(CancellationToken cancellationToken);
    ValueTask PauseAsync(CancellationToken cancellationToken);
    ValueTask ResumeAsync(CancellationToken cancellationToken);
    ValueTask DrainAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
