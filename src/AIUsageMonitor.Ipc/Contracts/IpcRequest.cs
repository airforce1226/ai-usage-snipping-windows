using System.Text.Json;

namespace AIUsageMonitor.Ipc.Contracts;

public sealed record IpcRequest(
    int ProtocolVersion,
    string RequestId,
    string Type,
    JsonElement Payload);
