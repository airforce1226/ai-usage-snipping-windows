using System.Text;
using AIUsageMonitor.Infrastructure.Providers;

namespace AIUsageMonitor.Infrastructure.Tests.Providers;

public sealed class JsonLineStreamReaderTests
{
    [Fact]
    public async Task ReadAsync_DoesNotConsumeIncompleteFinalLine()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"a\":1}\r\n{\"b\":2}");
        await using var stream = new MemoryStream(bytes);
        var reader = new JsonLineStreamReader();

        var result = await reader.ReadAsync(stream, 0, CancellationToken.None);

        var line = Assert.Single(result.Lines);
        Assert.Equal("{\"a\":1}", line.Text);
        Assert.Equal(9, result.CompleteByteOffset);
    }
}
