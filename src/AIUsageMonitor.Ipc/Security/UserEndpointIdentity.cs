using System.Security.Cryptography;
using System.Text;

namespace AIUsageMonitor.Ipc.Security;

public sealed record UserEndpointIdentity(string PipeName, string MutexName)
{
    public static UserEndpointIdentity Create(string sid, int protocolMajor)
    {
        byte[] sidHash = SHA256.HashData(Encoding.UTF8.GetBytes(sid));
        string userHash = Convert.ToHexString(sidHash, 0, 8).ToLowerInvariant();
        string endpointName = $"AIUsageMonitor.Agent.v{protocolMajor}.{userHash}";

        return new UserEndpointIdentity(endpointName, $@"Local\{endpointName}");
    }
}
