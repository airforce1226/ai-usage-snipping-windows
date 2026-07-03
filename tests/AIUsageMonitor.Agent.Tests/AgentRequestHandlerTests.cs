using System.Text.Json;
using AIUsageMonitor.Agent;
using AIUsageMonitor.Core.Collection;
using AIUsageMonitor.Ipc.Contracts;
namespace AIUsageMonitor.Agent.Tests;
public sealed class AgentRequestHandlerTests
{
 [Fact] public async Task MutationDuringStoppingReturnsStableErrorWithoutCollectionCall(){var c=new GateCollection();var r=new AgentRuntime(c,()=>DateTimeOffset.UtcNow,new ShutdownSignal());await r.StartAsync(default);var stop=r.ShutdownAsync(default);await c.Entered.Task;var h=new AgentRequestHandler(r);var response=await h.HandleAsync(new(1,"r","collection.pause",JsonDocument.Parse("{}").RootElement),default);Assert.Equal("agent_stopping",response.Error?.Code);Assert.Equal(1,c.CallCount);c.Release.SetResult();await stop;}
 private sealed class GateCollection:ICollectionControl { public int CallCount;public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public ValueTask ResumeAsync(CancellationToken t){CallCount++;return ValueTask.CompletedTask;}public ValueTask PauseAsync(CancellationToken t){CallCount++;return ValueTask.CompletedTask;}public ValueTask RefreshAsync(CancellationToken t){CallCount++;return ValueTask.CompletedTask;}public async ValueTask DrainAsync(TimeSpan x,CancellationToken t){Entered.SetResult();await Release.Task;} }
 private sealed class ShutdownSignal:IHostShutdownSignal{public void Signal(){}}
}
