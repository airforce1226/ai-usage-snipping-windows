using AIUsageMonitor.Agent;
using AIUsageMonitor.Core.Collection;
using AIUsageMonitor.Infrastructure.Collection;
using AIUsageMonitor.Infrastructure.Persistence;
using AIUsageMonitor.Ipc.Security;
using AIUsageMonitor.Ipc.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace AIUsageMonitor.Agent.Tests;
public sealed class AgentCompositionTests
{
 [Fact] public void DefaultPathsFollowPerUserContract(){var paths=AgentPaths.CreateDefault();Assert.EndsWith(Path.Combine(".claude","projects"),paths.ClaudeRoot);Assert.EndsWith(".codex",paths.CodexRoot);Assert.EndsWith(Path.Combine("AIUsageMonitor","profiles","default","usage.db"),paths.DatabasePath);}
 [Fact] public void CompositionResolvesHostOwnedServices()
 {
  var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));var paths=new AgentPaths(Path.Combine(root,"claude"),Path.Combine(root,"codex"),Path.Combine(root,"usage.db"));
  var builder=Host.CreateApplicationBuilder();builder.Services.AddAgent(paths);builder.Services.AddSingleton<ICurrentUserIdentity>(new TestIdentity());using var host=builder.Build();
  Assert.Equal(Path.GetFullPath(paths.DatabasePath),host.Services.GetRequiredService<DatabaseConnectionFactory>().DatabasePath);
  Assert.Same(host.Services.GetRequiredService<CollectionCoordinator>(),host.Services.GetRequiredService<ICollectionControl>());
  Assert.NotNull(host.Services.GetRequiredService<DatabaseMigrator>());Assert.NotNull(host.Services.GetRequiredService<AgentRequestHandler>());Assert.NotNull(host.Services.GetRequiredService<NamedPipeAgentServer>());Assert.NotNull(host.Services.GetRequiredService<UserEndpointIdentity>());
 }
 private sealed class TestIdentity:ICurrentUserIdentity{public string GetSid()=>"test-sid";}
}
