namespace AIUsageMonitor.App.ViewModels;

public enum AgentConnectionStatus
{
    Connected,
    Offline,
}

public sealed record AgentConnectionState(AgentConnectionStatus Status)
{
    public static AgentConnectionState Connected { get; } = new(AgentConnectionStatus.Connected);
    public static AgentConnectionState Offline { get; } = new(AgentConnectionStatus.Offline);
}
