using AIUsageMonitor.Agent;
namespace AIUsageMonitor.Agent.Tests;
public sealed class SingleInstanceGuardTests
{
    [Fact] public void OnlyOneOwnsAndDisposalReleases(){var name="AIUsageMonitor-test-"+Guid.NewGuid();using(var first=SingleInstanceGuard.TryAcquire(name)){Assert.NotNull(first);Assert.Null(SingleInstanceGuard.TryAcquire(name));}using var next=SingleInstanceGuard.TryAcquire(name);Assert.NotNull(next);}
}
