using System.Runtime.Versioning;
using System.Security.Principal;

namespace AIUsageMonitor.Ipc.Security;

public sealed class WindowsCurrentUserIdentity : ICurrentUserIdentity
{
    private readonly Func<string?> currentSidProvider;

    [SupportedOSPlatform("windows")]
    public WindowsCurrentUserIdentity()
        : this(GetCurrentSid)
    {
    }

    public WindowsCurrentUserIdentity(Func<string?> currentSidProvider)
    {
        this.currentSidProvider = currentSidProvider;
    }

    public string GetSid()
    {
        return currentSidProvider()
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
    }

    [SupportedOSPlatform("windows")]
    private static string? GetCurrentSid()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value;
    }
}
