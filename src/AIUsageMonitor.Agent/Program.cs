using AIUsageMonitor.Agent;
using AIUsageMonitor.Infrastructure.Persistence;
using AIUsageMonitor.Ipc.Security;
using AIUsageMonitor.Ipc.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder=Host.CreateApplicationBuilder(args);builder.Services.AddAgent(AgentPaths.CreateDefault());
using var host=builder.Build();
var endpoint=host.Services.GetRequiredService<UserEndpointIdentity>();
using var guard=SingleInstanceGuard.TryAcquire(endpoint.MutexName);
if(guard is null)return 10;
await host.Services.GetRequiredService<DatabaseMigrator>().InitializeAsync(default);
var runtime=host.Services.GetRequiredService<AgentRuntime>();await runtime.StartAsync(default);
await host.StartAsync();
await host.Services.GetRequiredService<NamedPipeAgentServer>().RunAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
await host.StopAsync();
return runtime.ShutdownResult is { IsCompletedSuccessfully:true } result ? result.Result : 0;
