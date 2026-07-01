using System.Collections.Concurrent;
using System.IO.Pipes;
using AIUsageMonitor.Ipc.Contracts;

namespace AIUsageMonitor.Ipc.Transport;

public sealed class NamedPipeAgentServer
{
    private const int ProtocolVersion = 1;
    private static readonly HashSet<string> RecognizedCommands =
    [
        "agent.health.get", "agent.shutdown", "collection.pause", "collection.resume",
        "collection.refresh", "usage.summary.get", "usage.projects.get", "usage.models.get",
        "usage.sessions.get", "settings.get", "settings.update", "profile.list", "profile.select"
    ];

    private readonly string pipeName;
    private readonly IIpcRequestHandler requestHandler;
    private readonly SemaphoreSlim activeHandlers = new(8, 8);

    public NamedPipeAgentServer(string pipeName, IIpcRequestHandler requestHandler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        this.pipeName = pipeName;
        this.requestHandler = requestHandler ?? throw new ArgumentNullException(nameof(requestHandler));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var handlers = new ConcurrentBag<Task>();
        try
        {
            while (true)
            {
                await activeHandlers.WaitAsync(cancellationToken).ConfigureAwait(false);
                NamedPipeServerStream pipe = CreatePipe();
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    handlers.Add(HandleConnectionAsync(pipe, cancellationToken));
                    pipe = null!;
                }
                finally
                {
                    if (pipe is not null)
                    {
                        await pipe.DisposeAsync().ConfigureAwait(false);
                        activeHandlers.Release();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await Task.WhenAll(handlers).ConfigureAwait(false);
        }
    }

    private NamedPipeServerStream CreatePipe() => new(
        pipeName,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                IpcRequest request = await IpcFrameCodec.ReadAsync<IpcRequest>(pipe, cancellationToken).ConfigureAwait(false);
                IpcResponse response = await CreateResponseAsync(request, cancellationToken).ConfigureAwait(false);
                await IpcFrameCodec.WriteAsync(pipe, response, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                activeHandlers.Release();
            }
        }
    }

    private Task<IpcResponse> CreateResponseAsync(IpcRequest request, CancellationToken cancellationToken)
    {
        if (request.ProtocolVersion != ProtocolVersion)
        {
            return Task.FromResult(ErrorResponse(request, "unsupported_protocol", "Unsupported IPC protocol version."));
        }

        if (!RecognizedCommands.Contains(request.Type))
        {
            return Task.FromResult(ErrorResponse(request, "unknown_command", "Unknown IPC command."));
        }

        return requestHandler.HandleAsync(request, cancellationToken);
    }

    private static IpcResponse ErrorResponse(IpcRequest request, string code, string message) =>
        new(ProtocolVersion, request.RequestId, request.Type, null, new IpcError(code, message));
}
