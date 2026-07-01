namespace AIUsageMonitor.Core.Domain;

public sealed record TokenUsage
{
    public TokenUsage(long inputTokens, long outputTokens, long cacheReadTokens, long cacheWriteTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(cacheReadTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(cacheWriteTokens);

        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CacheReadTokens = cacheReadTokens;
        CacheWriteTokens = cacheWriteTokens;
    }

    public long InputTokens { get; }

    public long OutputTokens { get; }

    public long CacheReadTokens { get; }

    public long CacheWriteTokens { get; }

    public long Total => checked(InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens);
}
