using System.IO.Pipes;
using AIUsageMonitor.Ipc.Contracts;

namespace AIUsageMonitor.Ipc.Transport;

public sealed class NamedPipeAgentClient
{
    private readonly string pipeName;

    public NamedPipeAgentClient(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        this.pipeName = pipeName;
    }

    public async Task<IpcResponse> SendAsync(
        IpcRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCancellation.Token);
        await using var pipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            await pipe.ConnectAsync(operationCancellation.Token).ConfigureAwait(false);
            await IpcFrameCodec.WriteAsync(pipe, request, operationCancellation.Token).ConfigureAwait(false);
            return await IpcFrameCodec.ReadAsync<IpcResponse>(pipe, operationCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out connecting to or exchanging data with pipe '{pipeName}'.");
        }
    }
}
