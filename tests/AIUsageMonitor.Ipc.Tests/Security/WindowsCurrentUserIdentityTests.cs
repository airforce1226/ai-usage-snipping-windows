using AIUsageMonitor.Ipc.Security;

namespace AIUsageMonitor.Ipc.Tests.Security;

public sealed class WindowsCurrentUserIdentityTests
{
    [Fact]
    public void GetSid_ReturnsSidProvidedByCurrentUserLookup()
    {
        var identity = new WindowsCurrentUserIdentity(() => "S-1-5-21-1000");

        string sid = identity.GetSid();

        Assert.Equal("S-1-5-21-1000", sid);
    }

    [Fact]
    public void GetSid_ThrowsInvalidOperationExceptionWhenSidIsUnavailable()
    {
        var identity = new WindowsCurrentUserIdentity(() => null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(identity.GetSid);

        Assert.Equal("The current Windows user SID is unavailable.", exception.Message);
    }
}
