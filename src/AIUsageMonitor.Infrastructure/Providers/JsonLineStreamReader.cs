using System.Buffers;
using System.Text;

namespace AIUsageMonitor.Infrastructure.Providers;

public sealed class JsonLineStreamReader
{
    private const int BufferSize = 16 * 1024;
    private const int MaximumLineBytes = 4 * 1024 * 1024;

    public async ValueTask<JsonLineReadResult> ReadAsync(
        Stream stream,
        long startOffset,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(startOffset);

        if (!stream.CanSeek)
        {
            throw new ArgumentException("The JSONL stream must support seeking.", nameof(stream));
        }

        stream.Seek(startOffset, SeekOrigin.Begin);
        var readBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var lineBuffer = new ArrayBufferWriter<byte>();
        var lines = new List<JsonLine>();
        var currentLineOffset = startOffset;
        var completeByteOffset = startOffset;

        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(
                       readBuffer.AsMemory(0, BufferSize),
                       cancellationToken)) > 0)
            {
                for (var index = 0; index < bytesRead; index++)
                {
                    var value = readBuffer[index];
                    if (value == (byte)'\n')
                    {
                        var lineBytes = lineBuffer.WrittenMemory.ToArray();
                        var textLength = lineBytes.Length;
                        if (textLength > 0 && lineBytes[textLength - 1] == (byte)'\r')
                        {
                            textLength--;
                        }

                        lines.Add(new JsonLine(
                            Encoding.UTF8.GetString(lineBytes, 0, textLength),
                            currentLineOffset));
                        completeByteOffset += lineBuffer.WrittenCount + 1;
                        currentLineOffset = completeByteOffset;
                        lineBuffer.Clear();
                        continue;
                    }

                    if (lineBuffer.WrittenCount >= MaximumLineBytes)
                    {
                        throw new InvalidDataException(
                            $"JSONL line at byte offset {currentLineOffset} exceeds {MaximumLineBytes} bytes.");
                    }

                    lineBuffer.GetSpan(1)[0] = value;
                    lineBuffer.Advance(1);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
        }

        return new JsonLineReadResult(lines, completeByteOffset);
    }
}

public sealed record JsonLine(string Text, long ByteOffset);

public sealed record JsonLineReadResult(IReadOnlyList<JsonLine> Lines, long CompleteByteOffset);
