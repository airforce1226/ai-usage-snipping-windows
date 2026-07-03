using AIUsageMonitor.Core.Collection;
namespace AIUsageMonitor.Agent;
public sealed class AgentRuntime
{
 private readonly ICollectionControl collection; private readonly IHostShutdownSignal shutdown; private readonly SemaphoreSlim gate=new(1,1); private AgentHealth health;
 public AgentRuntime(ICollectionControl collection,Func<DateTimeOffset> clock,IHostShutdownSignal shutdown){this.collection=collection;this.shutdown=shutdown;health=new(AgentState.Starting,clock(),null,null);}
 public AgentHealth Health { get { lock(gate) return health; } }
 public async ValueTask StartAsync(CancellationToken token){await gate.WaitAsync(token);try{if(health.State!=AgentState.Starting)return;await collection.ResumeAsync(token);health=health with{State=AgentState.Running};}finally{gate.Release();}}
 public async ValueTask PauseAsync(CancellationToken token){await gate.WaitAsync(token);try{ThrowIfStopping();if(health.State==AgentState.Paused)return;await collection.PauseAsync(token);health=health with{State=AgentState.Paused};}finally{gate.Release();}}
 public async ValueTask ResumeAsync(CancellationToken token){await gate.WaitAsync(token);try{ThrowIfStopping();if(health.State==AgentState.Running)return;await collection.ResumeAsync(token);health=health with{State=AgentState.Running};}finally{gate.Release();}}
 public async ValueTask RefreshAsync(CancellationToken token){await gate.WaitAsync(token);try{ThrowIfStopping();await collection.RefreshAsync(token);}finally{gate.Release();}}
 public async Task<int> ShutdownAsync(CancellationToken token){await gate.WaitAsync(token);try{if(health.State==AgentState.Stopping)return Environment.ExitCode;health=health with{State=AgentState.Stopping};}finally{gate.Release();}try{await collection.DrainAsync(TimeSpan.FromSeconds(10),token);Environment.ExitCode=0;return 0;}catch(TimeoutException){Environment.ExitCode=12;return 12;}finally{shutdown.Signal();}}
 private void ThrowIfStopping(){if(health.State==AgentState.Stopping)throw new AgentStoppingException();}
}
