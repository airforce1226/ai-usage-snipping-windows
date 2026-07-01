namespace AIUsageMonitor.Core.Domain;

public sealed record SourceCheckpoint(
    string CanonicalPath,
    long ByteOffset,
    long FileLength,
    DateTimeOffset LastWriteTimeUtc,
    string ParserVersion,
    IReadOnlyDictionary<string, string>? ProviderState = null);
