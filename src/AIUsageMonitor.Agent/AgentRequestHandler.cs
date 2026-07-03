using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageMonitor.Core.Queries;
using AIUsageMonitor.Ipc.Contracts;
using AIUsageMonitor.Ipc.Transport;
namespace AIUsageMonitor.Agent;
public sealed class AgentRequestHandler(AgentRuntime runtime,IUsageQueryService queryService) : IIpcRequestHandler, IIpcResponseCompletionHandler
{
 private static readonly JsonSerializerOptions StrictJsonOptions=new(UsageQueryJson.Options){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
 public async Task<IpcResponse> HandleAsync(IpcRequest request,CancellationToken token)
 {
  try
  {
   if(request.Type is UsageQueryCommands.SummaryGet or UsageQueryCommands.ProjectsGet or UsageQueryCommands.ModelsGet or UsageQueryCommands.SessionsGet)
   {
    try{return await HandleQueryAsync(request,token);}
    catch(JsonException){return Error(request,"invalid_payload","Invalid query payload.");}
   }
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
 private async Task<IpcResponse> HandleQueryAsync(IpcRequest request,CancellationToken token)
 {
  object payload;
  switch(request.Type)
  {
   case UsageQueryCommands.SummaryGet:
   {
    var value=DeserializeStrict<UsageSummaryRequest>(request.Payload);var error=Validate(request,value.Validate(),value.ProfileId);if(error is not null)return error;
    var result=await queryService.GetSummaryAsync(new(value.FromUtc,value.ToUtc),token);
    payload=new UsageSummaryResponse(result.InputTokens,result.OutputTokens,result.CacheReadTokens,result.CacheWriteTokens,result.EventCount,result.DatabaseUpdatedAtUtc);break;
   }
   case UsageQueryCommands.ProjectsGet:
   {
    var value=DeserializeStrict<UsageProjectsRequest>(request.Payload);var error=Validate(request,value.Validate(),value.ProfileId);if(error is not null)return error;
    var result=await queryService.GetProjectsAsync(Query(value.FromUtc,value.ToUtc,value.Offset,value.Limit),token);
    payload=new UsageProjectsResponse(result.Items.Select(x=>new ProjectUsagePayload(x.ProjectId,x.InputTokens,x.OutputTokens,x.CacheReadTokens,x.CacheWriteTokens,x.EventCount)).ToArray(),result.Offset,result.Limit,result.TotalCount,result.DatabaseUpdatedAtUtc);break;
   }
   case UsageQueryCommands.ModelsGet:
   {
    var value=DeserializeStrict<UsageModelsRequest>(request.Payload);var error=Validate(request,value.Validate(),value.ProfileId);if(error is not null)return error;
    var result=await queryService.GetModelsAsync(Query(value.FromUtc,value.ToUtc,value.Offset,value.Limit),token);
    payload=new UsageModelsResponse(result.Items.Select(x=>new ModelUsagePayload(x.Model,x.InputTokens,x.OutputTokens,x.CacheReadTokens,x.CacheWriteTokens,x.EventCount)).ToArray(),result.Offset,result.Limit,result.TotalCount,result.DatabaseUpdatedAtUtc);break;
   }
   default:
   {
    var value=DeserializeStrict<UsageSessionsRequest>(request.Payload);var error=Validate(request,value.Validate(),value.ProfileId);if(error is not null)return error;
    var result=await queryService.GetSessionsAsync(Query(value.FromUtc,value.ToUtc,value.Offset,value.Limit),token);
    payload=new UsageSessionsResponse(result.Items.Select(x=>new SessionUsagePayload(x.SessionId,x.ProjectId,x.InputTokens,x.OutputTokens,x.CacheReadTokens,x.CacheWriteTokens,x.EventCount,x.LastEventUtc)).ToArray(),result.Offset,result.Limit,result.TotalCount,result.DatabaseUpdatedAtUtc);break;
   }
  }
  return new(1,request.RequestId,request.Type,JsonSerializer.SerializeToElement(payload,UsageQueryJson.Options),null);
 }
 private static T DeserializeStrict<T>(JsonElement payload)=>payload.Deserialize<T>(StrictJsonOptions)??throw new JsonException("Payload cannot be null.");
 private static IpcResponse? Validate(IpcRequest request,UsageQueryValidationResult validation,string profileId)
 {
  if(!validation.IsValid)return Error(request,validation.ErrorCode!,"Invalid query request.");
  return profileId=="default"?null:Error(request,"profile_not_found","Profile was not found.");
 }
 private static PagedUsageQuery Query(DateTimeOffset fromUtc,DateTimeOffset toUtc,int offset,int limit)=>new(new(fromUtc,toUtc),new(offset,limit));
 private static async ValueTask<object> Run(ValueTask task){await task;return new { Accepted=true };}
 private static IpcResponse Error(IpcRequest request,string code,string message)=>new(1,request.RequestId,request.Type,null,new(code,message));
}
