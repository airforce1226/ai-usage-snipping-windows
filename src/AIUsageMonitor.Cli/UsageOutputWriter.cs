using System.Globalization;
using System.Text.Json;
using AIUsageMonitor.Core.Queries;

namespace AIUsageMonitor.Cli;

public static class UsageOutputWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteSummaryAsync(TextWriter writer, UsageSummary value, bool json)
    {
        if (json) { await WriteJsonAsync(writer, value); return; }
        await writer.WriteLineAsync(FormattableString.Invariant($"Input tokens:       {value.InputTokens}"));
        await writer.WriteLineAsync(FormattableString.Invariant($"Output tokens:      {value.OutputTokens}"));
        await writer.WriteLineAsync(FormattableString.Invariant($"Cache read tokens:  {value.CacheReadTokens}"));
        await writer.WriteLineAsync(FormattableString.Invariant($"Cache write tokens: {value.CacheWriteTokens}"));
        await writer.WriteLineAsync(FormattableString.Invariant($"Events:             {value.EventCount}"));
        await writer.WriteLineAsync($"Updated (UTC):      {value.DatabaseUpdatedAtUtc:O}");
    }

    public static Task WriteProjectsAsync(TextWriter writer, PagedUsageResult<ProjectUsage> value, bool json) =>
        json ? WriteJsonAsync(writer, value) : WriteRowsAsync(writer, "PROJECT\tTOTAL\tINPUT\tOUTPUT\tCACHE_READ\tCACHE_WRITE\tEVENTS",
            value.Items.Select(x => string.Join('\t', x.ProjectId, Number(x.TotalTokens), Number(x.InputTokens), Number(x.OutputTokens), Number(x.CacheReadTokens), Number(x.CacheWriteTokens), Number(x.EventCount))));

    public static Task WriteModelsAsync(TextWriter writer, PagedUsageResult<ModelUsage> value, bool json) =>
        json ? WriteJsonAsync(writer, value) : WriteRowsAsync(writer, "MODEL\tTOTAL\tINPUT\tOUTPUT\tCACHE_READ\tCACHE_WRITE\tEVENTS",
            value.Items.Select(x => string.Join('\t', x.Model, Number(x.TotalTokens), Number(x.InputTokens), Number(x.OutputTokens), Number(x.CacheReadTokens), Number(x.CacheWriteTokens), Number(x.EventCount))));

    public static Task WriteSessionsAsync(TextWriter writer, PagedUsageResult<SessionUsage> value, bool json) =>
        json ? WriteJsonAsync(writer, value) : WriteRowsAsync(writer, "SESSION\tPROJECT\tTOTAL\tEVENTS\tLAST_EVENT_UTC",
            value.Items.Select(x => string.Join('\t', x.SessionId, x.ProjectId, Number(x.TotalTokens), Number(x.EventCount), x.LastEventUtc.ToString("O"))));

    private static async Task WriteJsonAsync<T>(TextWriter writer, T value) =>
        await writer.WriteLineAsync(JsonSerializer.Serialize(value, JsonOptions));

    private static async Task WriteRowsAsync(TextWriter writer, string header, IEnumerable<string> rows)
    {
        await writer.WriteLineAsync(header);
        foreach (string row in rows) await writer.WriteLineAsync(row);
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
