using System.Text.Json;
using AIUsageMonitor.Ipc.Contracts;
using AIUsageMonitor.Ipc.Transport;
namespace AIUsageMonitor.Agent;
public sealed class AgentRequestHandler(AgentRuntime runtime) : IIpcRequestHandler, IIpcResponseCompletionHandler
{
 public async Task<IpcResponse> HandleAsync(IpcRequest request,CancellationToken token)
 {
  try
  {
   object? payload=request.Type switch
   {
    "agent.health.get"=>runtime.Health,
    "collection.pause"=>await Run(runtime.PauseAsync(token)),
    "collection.resume"=>await Run(runtime.ResumeAsync(token)),
    "collection.refresh"=>await Run(runtime.RefreshAsync(token)),
    "agent.shutdown"=>new { ExitCode=await runtime.ShutdownAsync(token) },
    _=>null
   };
   if(payload is null)return Error(request,"unknown_command","Unknown IPC command.");
   return new(1,request.RequestId,request.Type,JsonSerializer.SerializeToElement(payload),null);
  }
  catch(AgentStoppingException){return Error(request,"agent_stopping","Agent is stopping.");}
 }
 public ValueTask ResponseCompletedAsync(IpcRequest request,IpcResponse response,CancellationToken token){if(request.Type=="agent.shutdown"&&response.Error is null)runtime.CompleteShutdownResponse();return ValueTask.CompletedTask;}
 private static async ValueTask<object> Run(ValueTask task){await task;return new { Accepted=true };}
 private static IpcResponse Error(IpcRequest request,string code,string message)=>new(1,request.RequestId,request.Type,null,new(code,message));
}
