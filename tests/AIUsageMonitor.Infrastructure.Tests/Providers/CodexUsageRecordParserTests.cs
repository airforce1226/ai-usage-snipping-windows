using AIUsageMonitor.Core.Abstractions;
using AIUsageMonitor.Core.Domain;
using AIUsageMonitor.Infrastructure.Providers.Codex;

namespace AIUsageMonitor.Infrastructure.Tests.Providers;

public sealed class CodexUsageRecordParserTests
{
    [Fact]
    public async Task ParseAsync_ConvertsCumulativeSnapshotsToDeltas()
    {
        await using var stream = File.OpenRead(FixturePath("codex", "usage-variants.jsonl"));
        var parser = new CodexUsageRecordParser();

        var result = await parser.ParseAsync(
            stream,
            new ParseContext("codex.jsonl", "beta", 0, "codex-v1"),
            CancellationToken.None);

        Assert.Collection(
            result.Events,
            first =>
            {
                Assert.Equal(ProviderKind.Codex, first.Provider);
                Assert.Equal("codex-session-1", first.SessionId);
                Assert.Equal("gpt-5-codex", first.Model);
                Assert.Equal(new TokenUsage(100, 20, 0, 0), first.Tokens);
            },
            second => Assert.Equal(new TokenUsage(50, 10, 0, 0), second.Tokens));
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task ParseAsync_UsesCheckpointStateForIncrementalSnapshot()
    {
        const string line = "{\"type\":\"event_msg\",\"timestamp\":\"2026-07-01T02:03:00Z\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":175,\"output_tokens\":42}}}}\n";
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(line));
        var parser = new CodexUsageRecordParser();
        var state = new Dictionary<string, string>
        {
            ["sessionId"] = "codex-session-1",
            ["model"] = "gpt-5-codex",
            ["inputTokens"] = "150",
            ["outputTokens"] = "30",
        };

        var result = await parser.ParseAsync(
            stream,
            new ParseContext("codex.jsonl", "beta", 0, "codex-v1", state),
            CancellationToken.None);

        var usageEvent = Assert.Single(result.Events);
        Assert.Equal(new TokenUsage(25, 12, 0, 0), usageEvent.Tokens);
        Assert.Equal("175", result.NextProviderState["inputTokens"]);
        Assert.Equal("42", result.NextProviderState["outputTokens"]);
    }

    private static string FixturePath(params string[] segments)
    {
        return Path.Combine([AppContext.BaseDirectory, "Fixtures", .. segments]);
    }
}
