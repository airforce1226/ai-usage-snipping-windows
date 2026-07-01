using System.Globalization;
using System.Text.Json;
using AIUsageMonitor.Core.Abstractions;
using AIUsageMonitor.Core.Domain;

namespace AIUsageMonitor.Infrastructure.Providers.Codex;

public sealed class CodexUsageRecordParser : IUsageRecordParser
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
        var state = context.ProviderState;
        string? sessionId = GetState(state, "sessionId");
        var model = GetState(state, "model") ?? "codex-unknown";
        var previousInput = GetStateInt64(state, "inputTokens");
        var previousOutput = GetStateInt64(state, "outputTokens");

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
                var type = root.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : null;

                if (type == "session_meta" && root.TryGetProperty("payload", out var metadata))
                {
                    sessionId = metadata.TryGetProperty("id", out var id) ? id.GetString() : sessionId;
                    continue;
                }

                if (type == "turn_context" && root.TryGetProperty("payload", out var turnContext))
                {
                    model = turnContext.TryGetProperty("model", out var modelElement)
                        ? modelElement.GetString() ?? model
                        : model;
                    continue;
                }

                if (type != "event_msg"
                    || string.IsNullOrWhiteSpace(sessionId)
                    || !TryReadTokenSnapshot(root, out var timestamp, out var totalInput, out var totalOutput))
                {
                    continue;
                }

                var inputDelta = Math.Max(0, totalInput - previousInput);
                var outputDelta = Math.Max(0, totalOutput - previousOutput);
                previousInput = totalInput;
                previousOutput = totalOutput;

                events.Add(UsageEvent.Create(
                    ProviderKind.Codex,
                    sessionId,
                    $"token-count-{line.ByteOffset.ToString(CultureInfo.InvariantCulture)}",
                    context.ProjectId,
                    timestamp,
                    model,
                    new TokenUsage(inputDelta, outputDelta, 0, 0),
                    new SourceLocation(context.SourcePath, line.ByteOffset)));
            }
            catch (JsonException)
            {
                failures.Add(new ParseFailure(line.ByteOffset, "malformed_json"));
            }
        }

        var nextState = new Dictionary<string, string>
        {
            ["sessionId"] = sessionId ?? string.Empty,
            ["model"] = model,
            ["inputTokens"] = previousInput.ToString(CultureInfo.InvariantCulture),
            ["outputTokens"] = previousOutput.ToString(CultureInfo.InvariantCulture),
        };

        return new ParseResult(events, failures, input.CompleteByteOffset, nextState);
    }

    private static string? GetState(
        IReadOnlyDictionary<string, string>? state,
        string key)
    {
        return state is not null && state.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static long GetStateInt64(
        IReadOnlyDictionary<string, string>? state,
        string key)
    {
        return GetState(state, key) is { } value
            && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
                ? result
                : 0;
    }

    private static bool TryReadTokenSnapshot(
        JsonElement root,
        out DateTimeOffset timestamp,
        out long totalInput,
        out long totalOutput)
    {
        timestamp = default;
        totalInput = 0;
        totalOutput = 0;

        return root.TryGetProperty("timestamp", out var timestampElement)
            && DateTimeOffset.TryParse(timestampElement.GetString(), out timestamp)
            && root.TryGetProperty("payload", out var payload)
            && payload.TryGetProperty("type", out var payloadType)
            && payloadType.GetString() == "token_count"
            && payload.TryGetProperty("info", out var info)
            && info.TryGetProperty("total_token_usage", out var total)
            && total.TryGetProperty("input_tokens", out var input)
            && input.TryGetInt64(out totalInput)
            && total.TryGetProperty("output_tokens", out var output)
            && output.TryGetInt64(out totalOutput);
    }
}
