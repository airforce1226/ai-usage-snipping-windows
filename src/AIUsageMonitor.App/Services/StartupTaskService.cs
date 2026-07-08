namespace AIUsageMonitor.App.Services;

public enum StartupTaskState
{
    Disabled,
    DisabledByUser,
    DisabledByPolicy,
    Enabled,
    EnabledByPolicy,
}

public interface IStartupTaskBridge
{
    ValueTask<StartupTaskState> GetStateAsync(CancellationToken cancellationToken);
    ValueTask<StartupTaskState> RequestEnableAsync(CancellationToken cancellationToken);
    ValueTask DisableAsync(CancellationToken cancellationToken);
}

public sealed class StartupTaskService(IStartupTaskBridge bridge)
{
    public ValueTask<StartupTaskState> GetStateAsync(CancellationToken cancellationToken) =>
        bridge.GetStateAsync(cancellationToken);

    public async ValueTask<StartupTaskState> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (enabled)
        {
            return await bridge.RequestEnableAsync(cancellationToken).ConfigureAwait(false);
        }

        await bridge.DisableAsync(cancellationToken).ConfigureAwait(false);
        return await bridge.GetStateAsync(cancellationToken).ConfigureAwait(false);
    }
}
