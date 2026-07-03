using AIUsageMonitor.Core.Collection;
namespace AIUsageMonitor.Agent;
public sealed class AgentRuntime
{
 private readonly ICollectionControl collection; private readonly ICollectionHealth? collectionHealth; private readonly IHostShutdownSignal shutdown; private readonly SemaphoreSlim gate=new(1,1);
 private AgentState state=AgentState.Starting; private readonly DateTimeOffset startedAtUtc; private Task<int>? shutdownTask; private int shutdownSignaled;
 public AgentRuntime(ICollectionControl collection,Func<DateTimeOffset> clock,IHostShutdownSignal shutdown){this.collection=collection;collectionHealth=collection as ICollectionHealth;this.shutdown=shutdown;startedAtUtc=clock();}
 public AgentHealth Health { get { gate.Wait();try{return new(state,startedAtUtc,collectionHealth?.LastSuccessfulCollectionUtc,collectionHealth?.LastSuccessfulDatabaseUtc);}finally{gate.Release();} } }
 public Task<int>? ShutdownResult { get { gate.Wait();try{return shutdownTask;}finally{gate.Release();} } }
 public async ValueTask StartAsync(CancellationToken token){await gate.WaitAsync(token);try{if(state!=AgentState.Starting)return;await collection.ResumeAsync(token);state=AgentState.Running;}catch{state=AgentState.Degraded;throw;}finally{gate.Release();}}
 public async ValueTask PauseAsync(CancellationToken token){await gate.WaitAsync(token);try{ThrowIfStopping();if(state==AgentState.Paused)return;if(state is not (AgentState.Running or AgentState.Degraded))throw new InvalidOperationException();await collection.PauseAsync(token);state=AgentState.Paused;}finally{gate.Release();}}
 public async ValueTask ResumeAsync(CancellationToken token){await gate.WaitAsync(token);try{ThrowIfStopping();if(state==AgentState.Running)return;if(state is not (AgentState.Paused or AgentState.Degraded))throw new InvalidOperationException();await collection.ResumeAsync(token);state=AgentState.Running;}catch(AgentStoppingException){throw;}catch{state=AgentState.Degraded;throw;}finally{gate.Release();}}
 public async ValueTask RefreshAsync(CancellationToken token){await gate.WaitAsync(token);try{ThrowIfStopping();if(state is not (AgentState.Running or AgentState.Paused))throw new InvalidOperationException();await collection.RefreshAsync(token);}finally{gate.Release();}}
 public Task<int> ShutdownAsync(CancellationToken token)
 {
  TaskCompletionSource<int>? completion=null;
  gate.Wait(token);
  try
  {
   if(shutdownTask is not null)return shutdownTask;
   state=AgentState.Stopping;
   completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
   shutdownTask=completion.Task;
  }
  finally { gate.Release(); }
  _=CompleteDrainAsync(completion,token);
  return completion.Task;
 }
 public void CompleteShutdownResponse(){if(shutdownTask is { IsCompleted:true }&&Interlocked.Exchange(ref shutdownSignaled,1)==0)shutdown.Signal();}
 private async Task CompleteDrainAsync(TaskCompletionSource<int> completion,CancellationToken token){try{await collection.DrainAsync(TimeSpan.FromSeconds(10),token);completion.TrySetResult(0);}catch(TimeoutException){completion.TrySetResult(12);}catch(Exception error){completion.TrySetException(error);}}
 private void ThrowIfStopping(){if(state==AgentState.Stopping)throw new AgentStoppingException();}
}
