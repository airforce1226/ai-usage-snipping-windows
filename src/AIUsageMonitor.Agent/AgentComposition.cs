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
namespace AIUsageMonitor.Agent;
public sealed record AgentPaths(string ClaudeRoot,string CodexRoot,string DatabasePath)
{
 public static AgentPaths CreateDefault(){var profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);return new(Path.Combine(profile,".claude","projects"),Path.Combine(profile,".codex"),Path.Combine(local,"AIUsageMonitor","profiles","default","usage.db"));}
}
public static class AgentComposition
{
 public static void AddAgent(this IServiceCollection services,AgentPaths paths)
 {
  services.AddSingleton<ICurrentUserIdentity,WindowsCurrentUserIdentity>();
  services.AddSingleton(sp=>UserEndpointIdentity.Create(sp.GetRequiredService<ICurrentUserIdentity>().GetSid(),1));
  services.AddSingleton(new DatabaseConnectionFactory(paths.DatabasePath));services.AddSingleton<DatabaseMigrator>();services.AddSingleton<SqliteUsageEventStore>();
  services.AddSingleton<IUsageEventStore>(sp=>sp.GetRequiredService<SqliteUsageEventStore>());services.AddSingleton<ISourceCheckpointStore>(sp=>sp.GetRequiredService<SqliteUsageEventStore>());
  services.AddSingleton<IncrementalFileReader>();services.AddSingleton<IProviderFileWatcherFactory,ProviderFileWatcherFactory>();
  services.AddSingleton(sp=>new CollectionCoordinator([new(ProviderKind.Claude,paths.ClaudeRoot,"default","1",new ClaudeUsageRecordParser()),new(ProviderKind.Codex,paths.CodexRoot,"default","1",new CodexUsageRecordParser())],sp.GetRequiredService<IncrementalFileReader>(),sp.GetRequiredService<IProviderFileWatcherFactory>()));
  services.AddSingleton<ICollectionControl>(sp=>sp.GetRequiredService<CollectionCoordinator>());
  services.AddSingleton<IHostShutdownSignal>(sp=>new HostShutdownSignal(sp.GetRequiredService<IHostApplicationLifetime>()));services.AddSingleton<Func<DateTimeOffset>>(()=>DateTimeOffset.UtcNow);
  services.AddSingleton<AgentRuntime>();services.AddSingleton<AgentRequestHandler>();services.AddSingleton<IIpcRequestHandler>(sp=>sp.GetRequiredService<AgentRequestHandler>());
  services.AddSingleton(sp=>new NamedPipeAgentServer(sp.GetRequiredService<UserEndpointIdentity>().PipeName,sp.GetRequiredService<IIpcRequestHandler>()));
 }
 private sealed class HostShutdownSignal(IHostApplicationLifetime lifetime):IHostShutdownSignal{public void Signal()=>lifetime.StopApplication();}
}
