namespace AIUsageMonitor.Core.Domain;

public sealed record SourceLocation
{
    public SourceLocation(string path, long byteOffset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);

        Path = path;
        ByteOffset = byteOffset;
    }

    public string Path { get; }

    public long ByteOffset { get; }
}
