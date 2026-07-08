using AIUsageMonitor.App.Services;
using AIUsageMonitor.App.ViewModels;

namespace AIUsageMonitor.App.Tests.Services;

public sealed class AgentConnectionServiceTests
{
    [Fact]
    public async Task ConnectAsync_LaunchesOnceAndUsesExactRetryDelays()
    {
        var client = new RecordingAgentClient(false, false, false, false);
        var launcher = new RecordingLauncher();
        var delay = new RecordingDelay();
        var service = new AgentConnectionService(client, launcher, delay, TimeProvider.System);

        AgentConnectionState state = await service.ConnectAsync(CancellationToken.None);

        Assert.Equal(AgentConnectionStatus.Offline, state.Status);
        Assert.Equal(4, client.Timeouts.Count);
        Assert.All(client.Timeouts, timeout => Assert.Equal(TimeSpan.FromMilliseconds(750), timeout));
        Assert.Equal([TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000)], delay.Delays);
        Assert.Equal(1, launcher.LaunchCount);
    }

    [Fact]
    public async Task ConnectAsync_ReturnsConnectedWhenRetrySucceeds()
    {
        var client = new RecordingAgentClient(false, true);
        var launcher = new RecordingLauncher();
        var delay = new RecordingDelay();
        var service = new AgentConnectionService(client, launcher, delay, TimeProvider.System);

        AgentConnectionState state = await service.ConnectAsync(CancellationToken.None);

        Assert.Equal(AgentConnectionStatus.Connected, state.Status);
        Assert.Single(delay.Delays);
        Assert.Equal(1, launcher.LaunchCount);
    }

    [Fact]
    public async Task ConnectAsync_LaunchesAtMostThreeTimesWithinTenMinutes()
    {
        var client = new RecordingAgentClient(Enumerable.Repeat(false, 16).ToArray());
        var launcher = new RecordingLauncher();
        var delay = new RecordingDelay();
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-07T00:00:00Z"));
        var service = new AgentConnectionService(client, launcher, delay, clock);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await service.ConnectAsync(CancellationToken.None);
        }

        Assert.Equal(3, launcher.LaunchCount);
    }

    private sealed class RecordingAgentClient(params bool[] results) : IAgentConnectionClient
    {
        private readonly Queue<bool> results = new(results);
        public List<TimeSpan> Timeouts { get; } = [];

        public ValueTask<bool> IsAvailableAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            Timeouts.Add(timeout);
            return ValueTask.FromResult(results.Count > 0 && results.Dequeue());
        }
    }

    private sealed class RecordingLauncher : IAgentProcessLauncher
    {
        public int LaunchCount { get; private set; }

        public ValueTask LaunchAsync(CancellationToken cancellationToken)
        {
            LaunchCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDelay : IAsyncDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
