using AIUsageMonitor.Infrastructure.Queries;
using AIUsageMonitor.Ipc.Client;
using AIUsageMonitor.Ipc.Security;
using AIUsageMonitor.Ipc.Transport;

namespace AIUsageMonitor.Cli;

public static class CliComposition
{
    public static CliApplication CreateDefault()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("AI Usage Monitor CLI requires Windows.");
        }

        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string sid = new WindowsCurrentUserIdentity().GetSid();
        return Create(sid, GetDefaultDatabasePath(localData), Console.Out, Console.Error, TimeProvider.System, TimeZoneInfo.Local);
    }

    public static CliApplication Create(string sid, string databasePath, TextWriter output, TextWriter error,
        TimeProvider timeProvider, TimeZoneInfo localZone)
    {
        UserEndpointIdentity endpoint = UserEndpointIdentity.Create(sid, 1);
        var agent = new AgentUsageQueryClient(new NamedPipeAgentClient(endpoint.PipeName), "default", TimeSpan.FromMilliseconds(500));
        var queries = new AgentOrDatabaseQueryClient(agent, new SqliteUsageQueryService(databasePath));
        return new CliApplication(queries, agent, output, error, timeProvider, localZone);
    }

    public static string GetDefaultDatabasePath(string localApplicationData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        return Path.Combine(localApplicationData, "AIUsageMonitor", "profiles", "default", "usage.db");
    }
}
