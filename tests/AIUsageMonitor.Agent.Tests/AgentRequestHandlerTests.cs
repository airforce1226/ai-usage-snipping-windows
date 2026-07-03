using System.Text.Json;
using AIUsageMonitor.Agent;
using AIUsageMonitor.Core.Collection;
using AIUsageMonitor.Core.Queries;
using AIUsageMonitor.Ipc.Contracts;
namespace AIUsageMonitor.Agent.Tests;
public sealed class AgentRequestHandlerTests
{
 private static AgentRequestHandler Handler(AgentRuntime runtime)=>new(runtime,new NoOpQueries());
 [Theory]
 [InlineData("collection.pause")]
 [InlineData("collection.resume")]
 [InlineData("collection.refresh")]
 public async Task MutationDuringStoppingReturnsStableErrorWithoutCollectionCall(string type){var c=new GateCollection();var r=new AgentRuntime(c,()=>DateTimeOffset.UtcNow,new ShutdownSignal());await r.StartAsync(default);var stop=r.ShutdownAsync(default);await c.Entered.Task;var h=Handler(r);var response=await h.HandleAsync(new(1,"r",type,JsonDocument.Parse("{}").RootElement),default);Assert.Equal("agent_stopping",response.Error?.Code);Assert.Equal(1,c.CallCount);c.Release.SetResult();await stop;}
 [Fact] public async Task ShutdownSignalsOnlyAfterResponseCompletion(){var c=new GateCollection();var signal=new ShutdownSignal();var r=new AgentRuntime(c,()=>DateTimeOffset.UtcNow,signal);await r.StartAsync(default);c.Release.SetResult();var h=Handler(r);var request=new IpcRequest(1,"r","agent.shutdown",JsonDocument.Parse("{}").RootElement);var response=await h.HandleAsync(request,default);Assert.Equal(0,signal.Count);await h.ResponseCompletedAsync(request,response,default);Assert.Equal(1,signal.Count);}
 private sealed class GateCollection:ICollectionControl { public int CallCount;public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public ValueTask ResumeAsync(CancellationToken t){CallCount++;return ValueTask.CompletedTask;}public ValueTask PauseAsync(CancellationToken t){CallCount++;return ValueTask.CompletedTask;}public ValueTask RefreshAsync(CancellationToken t){CallCount++;return ValueTask.CompletedTask;}public async ValueTask DrainAsync(TimeSpan x,CancellationToken t){Entered.SetResult();await Release.Task;} }
 private sealed class ShutdownSignal:IHostShutdownSignal{public int Count;public void Signal()=>Count++;}
 private sealed class NoOpQueries:IUsageQueryService { public ValueTask<UsageSummary> GetSummaryAsync(UsageQueryRange r,CancellationToken t)=>throw new NotSupportedException();public ValueTask<PagedUsageResult<ProjectUsage>> GetProjectsAsync(PagedUsageQuery q,CancellationToken t)=>throw new NotSupportedException();public ValueTask<PagedUsageResult<ModelUsage>> GetModelsAsync(PagedUsageQuery q,CancellationToken t)=>throw new NotSupportedException();public ValueTask<PagedUsageResult<SessionUsage>> GetSessionsAsync(PagedUsageQuery q,CancellationToken t)=>throw new NotSupportedException(); }
}
