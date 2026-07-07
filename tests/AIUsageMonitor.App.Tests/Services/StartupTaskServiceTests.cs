using AIUsageMonitor.App.Services;

namespace AIUsageMonitor.App.Tests.Services;

public sealed class StartupTaskServiceTests
{
    [Fact]
    public async Task GetStateAsync_DoesNotRequestEnablementOnFirstLaunch()
    {
        var bridge = new RecordingStartupTaskBridge(StartupTaskState.Disabled);
        var service = new StartupTaskService(bridge);

        StartupTaskState state = await service.GetStateAsync(CancellationToken.None);

        Assert.Equal(StartupTaskState.Disabled, state);
        Assert.Equal(0, bridge.EnableRequests);
    }

    [Fact]
    public async Task SetEnabledAsync_RequestsEnablementOnlyAfterExplicitOptIn()
    {
        var bridge = new RecordingStartupTaskBridge(StartupTaskState.DisabledByUser);
        var service = new StartupTaskService(bridge);

        StartupTaskState state = await service.SetEnabledAsync(true, CancellationToken.None);

        Assert.Equal(StartupTaskState.DisabledByUser, state);
        Assert.Equal(1, bridge.EnableRequests);
    }

    [Fact]
    public async Task SetEnabledAsync_DisablesOnOptOutAndReturnsResultingState()
    {
        var bridge = new RecordingStartupTaskBridge(StartupTaskState.Enabled);
        var service = new StartupTaskService(bridge);

        StartupTaskState state = await service.SetEnabledAsync(false, CancellationToken.None);

        Assert.Equal(StartupTaskState.Disabled, state);
        Assert.Equal(1, bridge.DisableRequests);
    }

    private sealed class RecordingStartupTaskBridge(StartupTaskState state) : IStartupTaskBridge
    {
        private StartupTaskState state = state;
        public int EnableRequests { get; private set; }
        public int DisableRequests { get; private set; }

        public ValueTask<StartupTaskState> GetStateAsync(CancellationToken cancellationToken) => ValueTask.FromResult(state);

        public ValueTask<StartupTaskState> RequestEnableAsync(CancellationToken cancellationToken)
        {
            EnableRequests++;
            return ValueTask.FromResult(state);
        }

        public ValueTask DisableAsync(CancellationToken cancellationToken)
        {
            DisableRequests++;
            state = StartupTaskState.Disabled;
            return ValueTask.CompletedTask;
        }
    }
}
