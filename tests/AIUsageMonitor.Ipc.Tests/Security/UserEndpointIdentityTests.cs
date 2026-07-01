using AIUsageMonitor.Ipc.Security;

namespace AIUsageMonitor.Ipc.Tests.Security;

public sealed class UserEndpointIdentityTests
{
    [Fact]
    public void Create_UsesExpectedNamesForSidAndProtocolVersion()
    {
        UserEndpointIdentity identity = UserEndpointIdentity.Create("S-1-5-21-1000", 1);

        Assert.Equal("AIUsageMonitor.Agent.v1.f051b5cbf3c10c7c", identity.PipeName);
        Assert.Equal(@"Local\AIUsageMonitor.Agent.v1.f051b5cbf3c10c7c", identity.MutexName);
    }

    [Fact]
    public void Create_ReturnsEqualNamesForEqualSidAndVersion()
    {
        UserEndpointIdentity first = UserEndpointIdentity.Create("S-1-5-21-1000", 1);
        UserEndpointIdentity second = UserEndpointIdentity.Create("S-1-5-21-1000", 1);

        Assert.Equal(first.PipeName, second.PipeName);
        Assert.Equal(first.MutexName, second.MutexName);
    }

    [Fact]
    public void Create_ReturnsDifferentNamesForDifferentSid()
    {
        UserEndpointIdentity first = UserEndpointIdentity.Create("S-1-5-21-1000", 1);
        UserEndpointIdentity second = UserEndpointIdentity.Create("S-1-5-21-2000", 1);

        Assert.NotEqual(first.PipeName, second.PipeName);
        Assert.NotEqual(first.MutexName, second.MutexName);
    }

    [Fact]
    public void Create_DoesNotExposeRawSidInNames()
    {
        const string sid = "S-1-5-21-1000";

        UserEndpointIdentity identity = UserEndpointIdentity.Create(sid, 1);

        Assert.DoesNotContain(sid, identity.PipeName, StringComparison.Ordinal);
        Assert.DoesNotContain(sid, identity.MutexName, StringComparison.Ordinal);
        Assert.Matches(@"^AIUsageMonitor\.Agent\.v1\.[0-9a-f]{16}$", identity.PipeName);
    }

    [Fact]
    public void Create_UsesSha256ForAnotherSid()
    {
        UserEndpointIdentity identity = UserEndpointIdentity.Create("S-1-5-18", 1);

        Assert.Equal("AIUsageMonitor.Agent.v1.593347bdfcc9bfa7", identity.PipeName);
    }
}
