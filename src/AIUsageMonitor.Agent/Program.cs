using AIUsageMonitor.Agent;
using AIUsageMonitor.Core.Abstractions;
using AIUsageMonitor.Core.Collection;
using AIUsageMonitor.Core.Domain;
using AIUsageMonitor.Infrastructure.Collection;
using AIUsageMonitor.Infrastructure.Persistence;
using AIUsageMonitor.Infrastructure.Providers.Claude;
using AIUsageMonitor.Infrastructure.Providers.Codex;
using AIUsageMonitor.Ipc.Security;
using AIUsageMonitor.Ipc.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var identity=UserEndpointIdentity.Create(new WindowsCurrentUserIdentity().GetSid(),1);
using var guard=SingleInstanceGuard.TryAcquire(identity.MutexName);
if(guard is null)return 10;
var builder=Host.CreateApplicationBuilder(args);
var profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var factory=new DatabaseConnectionFactory(Path.Combine(local,"AIUsageMonitor","profiles","default","usage.db"));
await new DatabaseMigrator(factory).InitializeAsync(default);
var store=new SqliteUsageEventStore(factory);
var reader=new IncrementalFileReader(store,store);
ICollectionControl collection=new CollectionCoordinator([
 new(ProviderKind.Claude,Path.Combine(profile,".claude","projects"),"default","1",new ClaudeUsageRecordParser()),
 new(ProviderKind.Codex,Path.Combine(profile,".codex"),"default","1",new CodexUsageRecordParser())],reader,new ProviderFileWatcherFactory());
builder.Services.AddSingleton(collection);
builder.Services.AddSingleton<IHostShutdownSignal>(sp=>new HostShutdownSignal(sp.GetRequiredService<IHostApplicationLifetime>()));
builder.Services.AddSingleton<AgentRuntime>();
builder.Services.AddSingleton<AgentRequestHandler>();
builder.Services.AddSingleton<IIpcRequestHandler>(sp=>sp.GetRequiredService<AgentRequestHandler>());
using var host=builder.Build();
var runtime=host.Services.GetRequiredService<AgentRuntime>();
await runtime.StartAsync(default);
var server=new NamedPipeAgentServer(identity.PipeName,host.Services.GetRequiredService<IIpcRequestHandler>());
await host.StartAsync();
await server.RunAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
await host.StopAsync();
return Environment.ExitCode;

file sealed class HostShutdownSignal(IHostApplicationLifetime lifetime):IHostShutdownSignal { public void Signal()=>lifetime.StopApplication(); }
