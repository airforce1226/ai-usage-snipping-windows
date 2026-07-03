namespace AIUsageMonitor.Agent;
public enum AgentState { Starting, Running, Paused, Degraded, Stopping }
public sealed record AgentHealth(AgentState State, DateTimeOffset StartedAtUtc, DateTimeOffset? LastSuccessfulCollectionUtc, DateTimeOffset? LastSuccessfulDatabaseUtc);
public sealed class AgentStoppingException : InvalidOperationException { public AgentStoppingException() : base("Agent is stopping.") { } }
public interface IHostShutdownSignal { void Signal(); }
