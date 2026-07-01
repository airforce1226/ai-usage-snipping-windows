using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using AIUsageMonitor.Ipc.Contracts;
using AIUsageMonitor.Ipc.Transport;

namespace AIUsageMonitor.Ipc.Tests.Transport;

public sealed class NamedPipeRoundTripTests
{
    [Fact]
    public async Task HealthRequestRoundTripsRequestIdentity()
    {
        await using var fixture = new ServerFixture();
        IpcRequest request = CreateRequest("health-1", "agent.health.get");

        IpcResponse response = await fixture.Client.SendAsync(request, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(request.RequestId, response.RequestId);
        Assert.Equal(request.Type, response.Type);
    }

    [Fact]
    public async Task UnsupportedProtocolReturnsError()
    {
        await using var fixture = new ServerFixture();

        IpcResponse response = await fixture.Client.SendAsync(
            CreateRequest("version-1", "agent.health.get", 2), TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal("unsupported_protocol", response.Error?.Code);
    }

    [Fact]
    public async Task UnknownCommandReturnsError()
    {
        await using var fixture = new ServerFixture();

        IpcResponse response = await fixture.Client.SendAsync(
            CreateRequest("unknown-1", "agent.dance"), TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal("unknown_command", response.Error?.Code);
    }

    [Fact]
    public async Task ClientHonorsConnectTimeout()
    {
        var client = new NamedPipeAgentClient(UniquePipeName());
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<TimeoutException>(() => client.SendAsync(
            CreateRequest("timeout-1", "agent.health.get"), TimeSpan.FromMilliseconds(150), CancellationToken.None));

        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(75), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ConcurrentClientsReceiveTheirOwnRequestIds()
    {
        await using var fixture = new ServerFixture();
        string[] requestIds = Enumerable.Range(1, 5).Select(index => $"concurrent-{index}").ToArray();

        IpcResponse[] responses = await Task.WhenAll(requestIds.Select(requestId =>
            fixture.Client.SendAsync(CreateRequest(requestId, "agent.health.get"), TimeSpan.FromSeconds(2), CancellationToken.None)));

        Assert.Equal(requestIds.Order(), responses.Select(response => response.RequestId).Order());
    }

    private static IpcRequest CreateRequest(string requestId, string type, int protocolVersion = 1)
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        return new IpcRequest(protocolVersion, requestId, type, document.RootElement.Clone());
    }

    private static string UniquePipeName() => $"AIUsageMonitor.Tests.{Guid.NewGuid():N}";

    private sealed class ServerFixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task serverTask;

        public ServerFixture()
        {
            string pipeName = UniquePipeName();
            var server = new NamedPipeAgentServer(pipeName, new EchoHandler());
            Client = new NamedPipeAgentClient(pipeName);
            serverTask = server.RunAsync(cancellation.Token);
        }

        public NamedPipeAgentClient Client { get; }

        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Dispose();
        }
    }

    private sealed class EchoHandler : IIpcRequestHandler
    {
        public Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new IpcResponse(1, request.RequestId, request.Type, request.Payload, null));
    }
}
