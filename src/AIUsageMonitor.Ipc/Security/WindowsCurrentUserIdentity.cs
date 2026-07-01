using System.Runtime.Versioning;
using System.Security.Principal;

namespace AIUsageMonitor.Ipc.Security;

[SupportedOSPlatform("windows")]
public sealed class WindowsCurrentUserIdentity : ICurrentUserIdentity
{
    public string GetSid()
    {
        return WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
    }
}
