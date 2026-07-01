using AIUsageMonitor.Core.Domain;

namespace AIUsageMonitor.Core.Tests.Domain;

public sealed class UsageEventTests
{
    [Fact]
    public void Create_RejectsNegativeTokenCount()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => UsageEvent.Create(
            ProviderKind.Claude,
            "session-1",
            "message-1",
            "project-1",
            DateTimeOffset.Parse("2026-07-01T10:00:00+09:00"),
            "claude-sonnet-4-6",
            new TokenUsage(-1, 2, 3, 4),
            new SourceLocation("C:/logs/a.jsonl", 42)));

        Assert.Equal("inputTokens", exception.ParamName);
    }

    [Fact]
    public void Total_IncludesEveryTokenCategory()
    {
        var usage = new TokenUsage(10, 20, 30, 40);

        Assert.Equal(100, usage.Total);
    }

    [Fact]
    public void Create_NormalizesTimestampToUtc()
    {
        var usageEvent = CreateEvent(DateTimeOffset.Parse("2026-07-01T10:00:00+09:00"));

        Assert.Equal(TimeSpan.Zero, usageEvent.Timestamp.Offset);
        Assert.Equal(DateTimeOffset.Parse("2026-07-01T01:00:00Z"), usageEvent.Timestamp);
    }

    [Fact]
    public void Create_GeneratesDeterministicLowercaseStableKey()
    {
        var first = CreateEvent(DateTimeOffset.Parse("2026-07-01T01:00:00Z"));
        var second = CreateEvent(DateTimeOffset.Parse("2026-07-01T01:00:00Z"));

        Assert.Equal(first.StableKey, second.StableKey);
        Assert.Matches("^[0-9a-f]{64}$", first.StableKey);
    }

    private static UsageEvent CreateEvent(DateTimeOffset timestamp)
    {
        return UsageEvent.Create(
            ProviderKind.Claude,
            "session-1",
            "message-1",
            "project-1",
            timestamp,
            "claude-sonnet-4-6",
            new TokenUsage(10, 20, 30, 40),
            new SourceLocation("C:/logs/a.jsonl", 42));
    }
}
