using AIUsageMonitor.Ipc.Contracts;

namespace AIUsageMonitor.Ipc.Transport;

public interface IIpcRequestHandler
{
    Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken cancellationToken);
}

public interface IIpcResponseCompletionHandler
{
    ValueTask ResponseCompletedAsync(IpcRequest request, IpcResponse response, CancellationToken cancellationToken);
}
