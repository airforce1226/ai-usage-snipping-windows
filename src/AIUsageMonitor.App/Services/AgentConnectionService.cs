using System.Text.Json;
using AIUsageMonitor.App.ViewModels;
using AIUsageMonitor.Ipc.Contracts;
using AIUsageMonitor.Ipc.Transport;

namespace AIUsageMonitor.App.Services;

public interface IAgentConnectionClient
{
    ValueTask<bool> IsAvailableAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public interface IAsyncDelay
{
    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemAsyncDelay : IAsyncDelay
{
    public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
}

public sealed class NamedPipeAgentConnectionClient(NamedPipeAgentClient client) : IAgentConnectionClient
{
    public async ValueTask<bool> IsAvailableAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var request = new IpcRequest(1, Guid.NewGuid().ToString("N"), "agent.health.get", JsonSerializer.SerializeToElement(new { }));
        try
        {
            IpcResponse response = await client.SendAsync(request, timeout, cancellationToken).ConfigureAwait(false);
            return response.Error is null;
        }
        catch (Exception exception) when (AgentConnectionService.IsUnavailable(exception))
        {
            return false;
        }
    }
}

public sealed class AgentConnectionService(
    IAgentConnectionClient client,
    IAgentProcessLauncher launcher,
    IAsyncDelay delay,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000),
    ];

    private readonly Queue<DateTimeOffset> launchTimes = new();

    public async ValueTask<AgentConnectionState> ConnectAsync(CancellationToken cancellationToken)
    {
        if (await client.IsAvailableAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false))
        {
            return AgentConnectionState.Connected;
        }

        if (CanLaunch())
        {
            await launcher.LaunchAsync(cancellationToken).ConfigureAwait(false);
            launchTimes.Enqueue(timeProvider.GetUtcNow());
        }

        foreach (TimeSpan retryDelay in RetryDelays)
        {
            await delay.DelayAsync(retryDelay, cancellationToken).ConfigureAwait(false);
            if (await client.IsAvailableAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false))
            {
                return AgentConnectionState.Connected;
            }
        }

        return AgentConnectionState.Offline;
    }

    internal static bool IsUnavailable(Exception exception) =>
        exception is TimeoutException or IOException or UnauthorizedAccessException;

    private bool CanLaunch()
    {
        DateTimeOffset cutoff = timeProvider.GetUtcNow() - RestartWindow;
        while (launchTimes.TryPeek(out DateTimeOffset launchedAt) && launchedAt <= cutoff)
        {
            launchTimes.Dequeue();
        }

        return launchTimes.Count < 3;
    }
}
