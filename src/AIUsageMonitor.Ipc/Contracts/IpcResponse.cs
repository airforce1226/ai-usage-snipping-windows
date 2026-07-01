using System.Text.Json;

namespace AIUsageMonitor.Ipc.Contracts;

public sealed record IpcResponse(
    int ProtocolVersion,
    string RequestId,
    string Type,
    JsonElement? Payload,
    IpcError? Error);
