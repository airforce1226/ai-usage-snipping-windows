using Windows.ApplicationModel;

namespace AIUsageMonitor.App.Services;

public sealed class WindowsStartupTaskBridge : IStartupTaskBridge
{
    public const string TaskId = "AIUsageMonitorAgentStartup";

    public async ValueTask<StartupTaskState> GetStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartupTask startupTask = await StartupTask.GetAsync(TaskId);
        return Map(startupTask.State);
    }

    public async ValueTask<StartupTaskState> RequestEnableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartupTask startupTask = await StartupTask.GetAsync(TaskId);
        return Map(await startupTask.RequestEnableAsync());
    }

    public async ValueTask DisableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartupTask startupTask = await StartupTask.GetAsync(TaskId);
        startupTask.Disable();
    }

    private static StartupTaskState Map(Windows.ApplicationModel.StartupTaskState state) => state switch
    {
        Windows.ApplicationModel.StartupTaskState.Disabled => StartupTaskState.Disabled,
        Windows.ApplicationModel.StartupTaskState.DisabledByUser => StartupTaskState.DisabledByUser,
        Windows.ApplicationModel.StartupTaskState.DisabledByPolicy => StartupTaskState.DisabledByPolicy,
        Windows.ApplicationModel.StartupTaskState.Enabled => StartupTaskState.Enabled,
        Windows.ApplicationModel.StartupTaskState.EnabledByPolicy => StartupTaskState.EnabledByPolicy,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown StartupTask state."),
    };
}
