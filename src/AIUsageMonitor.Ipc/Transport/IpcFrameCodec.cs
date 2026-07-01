using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace AIUsageMonitor.Ipc.Transport;

public static class IpcFrameCodec
{
    public const int MaximumPayloadLength = 1_048_576;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value);
        ValidatePayloadLength(payload.Length);

        byte[] prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        byte[] prefix = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        ValidatePayloadLength(payloadLength);

        byte[] rentedPayload = ArrayPool<byte>.Shared.Rent(payloadLength);
        try
        {
            Memory<byte> payload = rentedPayload.AsMemory(0, payloadLength);
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            string json = StrictUtf8.GetString(payload.Span);
            return JsonSerializer.Deserialize<T>(json)
                ?? throw new InvalidDataException("The IPC payload deserialized to null.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedPayload);
        }
    }

    private static void ValidatePayloadLength(int payloadLength)
    {
        if (payloadLength < 1)
        {
            throw new InvalidDataException("IPC payload length must be at least one byte.");
        }

        if (payloadLength > MaximumPayloadLength)
        {
            throw new InvalidDataException($"IPC payload exceeds the maximum of {MaximumPayloadLength} bytes.");
        }
    }
}
