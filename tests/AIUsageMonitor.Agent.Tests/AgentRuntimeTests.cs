using AIUsageMonitor.Agent;
using AIUsageMonitor.Core.Collection;

namespace AIUsageMonitor.Agent.Tests;

public sealed class AgentRuntimeTests
{
    [Fact]
    public async Task StartsByResumingAndReportsInitialHealth()
    {
        var collection = new RecordingCollection();
        var started = new DateTimeOffset(2026, 7, 3, 1, 2, 3, TimeSpan.Zero);
        var runtime = new AgentRuntime(collection, () => started, new RecordingShutdown());
        Assert.Equal(AgentState.Starting, runtime.Health.State);
        Assert.Equal(started, runtime.Health.StartedAtUtc);
        Assert.Null(runtime.Health.LastSuccessfulCollectionUtc);
        Assert.Null(runtime.Health.LastSuccessfulDatabaseUtc);
        await runtime.StartAsync(default);
        Assert.Equal(AgentState.Running, runtime.Health.State);
        Assert.Equal(["resume"], collection.Calls);
    }

    [Fact]
    public async Task PauseAndResumeAreIdempotentAndOrdered()
    {
        var collection = new RecordingCollection();
        var runtime = Create(collection);
        await runtime.StartAsync(default);
        await runtime.PauseAsync(default); await runtime.PauseAsync(default);
        await runtime.ResumeAsync(default); await runtime.ResumeAsync(default);
        Assert.Equal(["resume", "pause", "resume"], collection.Calls);
    }

    [Fact]
    public async Task RefreshWorksWhileRunningAndPausedButNotStopping()
    {
        var collection = new RecordingCollection(); var runtime = Create(collection);
        await runtime.StartAsync(default); await runtime.RefreshAsync(default);
        await runtime.PauseAsync(default); await runtime.RefreshAsync(default);
        _ = await runtime.ShutdownAsync(default);
        await Assert.ThrowsAsync<AgentStoppingException>(() => runtime.RefreshAsync(default).AsTask());
        Assert.Equal(2, collection.Calls.Count(x => x == "refresh"));
    }

    [Fact]
    public async Task ShutdownChangesStateBeforeExactDrainAndSignalsAfterward()
    {
        var shutdown = new RecordingShutdown(); var collection = new RecordingCollection();
        var runtime = new AgentRuntime(collection, () => DateTimeOffset.UtcNow, shutdown);
        await runtime.StartAsync(default);
        collection.OnDrain = () => Assert.Equal(AgentState.Stopping, runtime.Health.State);
        Assert.Equal(0, await runtime.ShutdownAsync(default));
        Assert.Equal(TimeSpan.FromSeconds(10), collection.DrainTimeout);
        Assert.Empty(shutdown.Calls);
        runtime.CompleteShutdownResponse();
        Assert.Equal(["signal"], shutdown.Calls);
    }

    [Fact]
    public async Task DrainTimeoutMapsToExitTwelveAndStillSignals()
    {
        var shutdown = new RecordingShutdown(); var collection = new RecordingCollection { ThrowTimeout = true };
        var runtime = new AgentRuntime(collection, () => DateTimeOffset.UtcNow, shutdown);
        await runtime.StartAsync(default);
        Assert.Equal(12, await runtime.ShutdownAsync(default));
        runtime.CompleteShutdownResponse();
        Assert.Equal(["signal"], shutdown.Calls);
    }

    [Fact]
    public async Task ConcurrentShutdownCallsShareOneTaskAndOneDrain()
    {
        var collection=new RecordingCollection();var runtime=Create(collection);await runtime.StartAsync(default);
        var first=runtime.ShutdownAsync(default);var second=runtime.ShutdownAsync(default);
        Assert.Same(first,second);Assert.Equal(0,await first);Assert.Equal(1,collection.Calls.Count(x=>x=="drain"));
    }

    private static AgentRuntime Create(RecordingCollection c) => new(c, () => DateTimeOffset.UtcNow, new RecordingShutdown());
    private sealed class RecordingCollection : ICollectionControl
    {
        public List<string> Calls { get; } = []; public TimeSpan DrainTimeout { get; private set; }
        public bool ThrowTimeout { get; init; } public Action? OnDrain { get; set; }
        public ValueTask PauseAsync(CancellationToken t) { Calls.Add("pause"); return ValueTask.CompletedTask; }
        public ValueTask ResumeAsync(CancellationToken t) { Calls.Add("resume"); return ValueTask.CompletedTask; }
        public ValueTask RefreshAsync(CancellationToken t) { Calls.Add("refresh"); return ValueTask.CompletedTask; }
        public ValueTask DrainAsync(TimeSpan timeout, CancellationToken t) { Calls.Add("drain"); DrainTimeout = timeout; OnDrain?.Invoke(); if (ThrowTimeout) throw new TimeoutException(); return ValueTask.CompletedTask; }
    }
    private sealed class RecordingShutdown : IHostShutdownSignal { public List<string> Calls { get; }=[]; public void Signal(){ Calls.Add("signal"); } }
}
