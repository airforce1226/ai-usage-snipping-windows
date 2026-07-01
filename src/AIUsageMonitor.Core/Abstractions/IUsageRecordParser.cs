using AIUsageMonitor.Core.Domain;

namespace AIUsageMonitor.Core.Abstractions;

public interface IUsageRecordParser
{
    ValueTask<ParseResult> ParseAsync(
        Stream stream,
        ParseContext context,
        CancellationToken cancellationToken);
}

public sealed record ParseContext(
    string SourcePath,
    string ProjectId,
    long StartOffset,
    string ParserVersion,
    IReadOnlyDictionary<string, string>? ProviderState = null);

public sealed record ParseFailure(long ByteOffset, string Category);

public sealed record ParseResult(
    IReadOnlyList<UsageEvent> Events,
    IReadOnlyList<ParseFailure> Failures,
    long CompleteByteOffset,
    IReadOnlyDictionary<string, string> NextProviderState);
