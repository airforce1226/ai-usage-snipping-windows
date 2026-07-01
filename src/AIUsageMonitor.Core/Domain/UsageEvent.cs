using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AIUsageMonitor.Core.Domain;

public sealed record UsageEvent
{
    private UsageEvent(
        ProviderKind provider,
        string sessionId,
        string? messageId,
        string projectId,
        DateTimeOffset timestamp,
        string model,
        TokenUsage tokens,
        SourceLocation source,
        string stableKey)
    {
        Provider = provider;
        SessionId = sessionId;
        MessageId = messageId;
        ProjectId = projectId;
        Timestamp = timestamp;
        Model = model;
        Tokens = tokens;
        Source = source;
        StableKey = stableKey;
    }

    public ProviderKind Provider { get; }

    public string SessionId { get; }

    public string? MessageId { get; }

    public string ProjectId { get; }

    public DateTimeOffset Timestamp { get; }

    public string Model { get; }

    public TokenUsage Tokens { get; }

    public SourceLocation Source { get; }

    public string StableKey { get; }

    public static UsageEvent Create(
        ProviderKind provider,
        string sessionId,
        string? messageId,
        string projectId,
        DateTimeOffset timestamp,
        string model,
        TokenUsage tokens,
        SourceLocation source)
    {
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(source);

        var utcTimestamp = timestamp.ToUniversalTime();
        var stableKey = CreateStableKey(
            provider,
            sessionId,
            messageId,
            utcTimestamp,
            model,
            source.ByteOffset);

        return new UsageEvent(
            provider,
            sessionId,
            messageId,
            projectId,
            utcTimestamp,
            model,
            tokens,
            source,
            stableKey);
    }

    private static string CreateStableKey(
        ProviderKind provider,
        string sessionId,
        string? messageId,
        DateTimeOffset timestamp,
        string model,
        long byteOffset)
    {
        var canonicalValue = string.Join(
            '\u001f',
            ((int)provider).ToString(CultureInfo.InvariantCulture),
            sessionId,
            messageId ?? string.Empty,
            timestamp.UtcTicks.ToString(CultureInfo.InvariantCulture),
            model,
            byteOffset.ToString(CultureInfo.InvariantCulture));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalValue));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
