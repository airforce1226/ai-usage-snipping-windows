using System.Text.Json;
using AIUsageMonitor.Core.Abstractions;
using AIUsageMonitor.Core.Domain;

namespace AIUsageMonitor.Infrastructure.Providers.Claude;

public sealed class ClaudeUsageRecordParser : IUsageRecordParser
{
    private readonly JsonLineStreamReader lineReader = new();

    public async ValueTask<ParseResult> ParseAsync(
        Stream stream,
        ParseContext context,
        CancellationToken cancellationToken)
    {
        var input = await lineReader.ReadAsync(stream, context.StartOffset, cancellationToken);
        var events = new List<UsageEvent>();
        var failures = new List<ParseFailure>();

        foreach (var line in input.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line.Text);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var type) || type.GetString() != "assistant")
                {
                    continue;
                }

                if (!TryReadEvent(root, context, line.ByteOffset, out var usageEvent))
                {
                    failures.Add(new ParseFailure(line.ByteOffset, "missing_usage_fields"));
                    continue;
                }

                events.Add(usageEvent);
            }
            catch (JsonException)
            {
                failures.Add(new ParseFailure(line.ByteOffset, "malformed_json"));
            }
        }

        return new ParseResult(
            events,
            failures,
            input.CompleteByteOffset,
            new Dictionary<string, string>());
    }

    private static bool TryReadEvent(
        JsonElement root,
        ParseContext context,
        long byteOffset,
        out UsageEvent usageEvent)
    {
        usageEvent = null!;
        if (!root.TryGetProperty("sessionId", out var sessionIdElement)
            || !root.TryGetProperty("timestamp", out var timestampElement)
            || !root.TryGetProperty("message", out var message)
            || !message.TryGetProperty("model", out var modelElement)
            || !message.TryGetProperty("usage", out var usage)
            || !usage.TryGetProperty("input_tokens", out var inputElement)
            || !usage.TryGetProperty("output_tokens", out var outputElement)
            || !DateTimeOffset.TryParse(timestampElement.GetString(), out var timestamp))
        {
            return false;
        }

        var sessionId = sessionIdElement.GetString();
        var model = modelElement.GetString();
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        usageEvent = UsageEvent.Create(
            ProviderKind.Claude,
            sessionId,
            root.TryGetProperty("uuid", out var uuid) ? uuid.GetString() : null,
            context.ProjectId,
            timestamp,
            model,
            new TokenUsage(
                inputElement.GetInt64(),
                outputElement.GetInt64(),
                GetOptionalInt64(usage, "cache_read_input_tokens"),
                GetOptionalInt64(usage, "cache_creation_input_tokens")),
            new SourceLocation(context.SourcePath, byteOffset));
        return true;
    }

    private static long GetOptionalInt64(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var result)
            ? result
            : 0;
    }
}
