using System.Diagnostics;

namespace AIUsageMonitor.App.Services;

public interface IAgentProcessLauncher
{
    ValueTask LaunchAsync(CancellationToken cancellationToken);
}

public sealed class AgentProcessLauncher(string executablePath) : IAgentProcessLauncher
{
    public ValueTask LaunchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        }) ?? throw new InvalidOperationException("The AI Usage Monitor Agent could not be started.");
        return ValueTask.CompletedTask;
    }
}
