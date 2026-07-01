using AIUsageMonitor.Core.Abstractions;
using AIUsageMonitor.Core.Domain;
using AIUsageMonitor.Infrastructure.Providers.Claude;

namespace AIUsageMonitor.Infrastructure.Tests.Providers;

public sealed class ClaudeUsageRecordParserTests
{
    [Fact]
    public async Task ParseAsync_EmitsAssistantUsageAndReportsMalformedLines()
    {
        await using var stream = File.OpenRead(FixturePath("claude", "usage-variants.jsonl"));
        var parser = new ClaudeUsageRecordParser();

        var result = await parser.ParseAsync(
            stream,
            new ParseContext("claude.jsonl", "alpha", 0, "claude-v1"),
            CancellationToken.None);

        Assert.Collection(
            result.Events,
            first =>
            {
                Assert.Equal(ProviderKind.Claude, first.Provider);
                Assert.Equal("message-1", first.MessageId);
                Assert.Equal("claude-session-1", first.SessionId);
                Assert.Equal(new TokenUsage(100, 20, 30, 10), first.Tokens);
            },
            second =>
            {
                Assert.Equal("message-3", second.MessageId);
                Assert.Equal(new TokenUsage(5, 7, 0, 0), second.Tokens);
            });
        Assert.Single(result.Failures);
        Assert.Equal("malformed_json", result.Failures[0].Category);
        Assert.Equal(stream.Length, result.CompleteByteOffset);
    }

    private static string FixturePath(params string[] segments)
    {
        return Path.Combine([AppContext.BaseDirectory, "Fixtures", .. segments]);
    }
}
