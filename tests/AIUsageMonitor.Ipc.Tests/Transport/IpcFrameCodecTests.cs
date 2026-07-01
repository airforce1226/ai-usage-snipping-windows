using System.Text;
using System.Text.Json;
using AIUsageMonitor.Ipc.Contracts;
using AIUsageMonitor.Ipc.Transport;

namespace AIUsageMonitor.Ipc.Tests.Transport;

public sealed class IpcFrameCodecTests
{
    [Fact]
    public async Task RoundTripsRequest()
    {
        using JsonDocument document = JsonDocument.Parse("{\"scope\":\"summary\"}");
        var request = new IpcRequest(1, "request-1", "usage.get", document.RootElement.Clone());
        await using var stream = new MemoryStream();
        await IpcFrameCodec.WriteAsync(stream, request, CancellationToken.None);
        stream.Position = 0;

        IpcRequest result = await IpcFrameCodec.ReadAsync<IpcRequest>(stream, CancellationToken.None);

        Assert.Equal(request.ProtocolVersion, result.ProtocolVersion);
        Assert.Equal(request.RequestId, result.RequestId);
        Assert.Equal(request.Type, result.Type);
        Assert.Equal("summary", result.Payload.GetProperty("scope").GetString());
    }

    [Fact]
    public async Task RejectsZeroLength()
    {
        await using var stream = new MemoryStream(BitConverter.GetBytes(0));
        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => IpcFrameCodec.ReadAsync<IpcRequest>(stream, CancellationToken.None));
        Assert.Contains("length", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsPayloadLargerThanMaximum()
    {
        await using var stream = new MemoryStream(BitConverter.GetBytes(1_048_577));
        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => IpcFrameCodec.ReadAsync<IpcRequest>(stream, CancellationToken.None));
        Assert.Contains("maximum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsTruncatedPayload()
    {
        byte[] frame = [5, 0, 0, 0, (byte)'{', (byte)'}'];
        await using var stream = new MemoryStream(frame);
        await Assert.ThrowsAsync<EndOfStreamException>(
            () => IpcFrameCodec.ReadAsync<IpcRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsMalformedUtf8()
    {
        byte[] frame = [2, 0, 0, 0, 0xC3, 0x28];
        await using var stream = new MemoryStream(frame);
        await Assert.ThrowsAsync<DecoderFallbackException>(
            () => IpcFrameCodec.ReadAsync<IpcRequest>(stream, CancellationToken.None));
    }
}
