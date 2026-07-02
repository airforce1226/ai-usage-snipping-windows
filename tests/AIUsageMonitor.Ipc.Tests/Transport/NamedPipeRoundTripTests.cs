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

    [Fact]
    public async Task CancellationStopsBlockedHandlerAndClientWithoutHanging()
    {
        string pipeName = UniquePipeName();
        var handler = new BlockingHandler();
        var server = new NamedPipeAgentServer(pipeName, handler);
        using var serverCancellation = new CancellationTokenSource();
        Task serverTask = server.RunAsync(serverCancellation.Token);
        var client = new NamedPipeAgentClient(pipeName);
        Task<IpcResponse> clientTask = client.SendAsync(
            CreateRequest("blocked-1", "agent.health.get"), TimeSpan.FromSeconds(5), CancellationToken.None);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        serverCancellation.Cancel();

        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<Exception>(() => clientTask.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(handler.CancellationObserved);
    }

    [Fact]
    public async Task HandlerFailureIsIsolatedAndServerServesNextClient()
    {
        string pipeName = UniquePipeName();
        var handler = new FailOnceHandler();
        var server = new NamedPipeAgentServer(pipeName, handler);
        using var cancellation = new CancellationTokenSource();
        Task serverTask = server.RunAsync(cancellation.Token);
        var client = new NamedPipeAgentClient(pipeName);

        await Assert.ThrowsAnyAsync<Exception>(() => client.SendAsync(
            CreateRequest("failure-1", "agent.health.get"), TimeSpan.FromSeconds(2), CancellationToken.None));
        IpcResponse response = await client.SendAsync(
            CreateRequest("success-1", "agent.health.get"), TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal("success-1", response.RequestId);
        cancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task AllowsEightHandlersAndWaitsToStartNinthUntilOneCompletes()
    {
        string pipeName = UniquePipeName();
        var handler = new GatedHandler();
        var server = new NamedPipeAgentServer(pipeName, handler);
        using var cancellation = new CancellationTokenSource();
        Task serverTask = server.RunAsync(cancellation.Token);
        var client = new NamedPipeAgentClient(pipeName);
        Task<IpcResponse>[] clients = Enumerable.Range(1, 9).Select(index => client.SendAsync(
            CreateRequest($"limit-{index}", "agent.health.get"), TimeSpan.FromSeconds(5), CancellationToken.None)).ToArray();
        await handler.EightStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await Task.Delay(100);
        Assert.Equal(8, handler.StartedCount);
        handler.ReleaseOne();
        await handler.NinthStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        handler.ReleaseAll();
        await Task.WhenAll(clients).WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
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

    private sealed class BlockingHandler : IIpcRequestHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public async Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }

            throw new InvalidOperationException();
        }
    }

    private sealed class FailOnceHandler : IIpcRequestHandler
    {
        private int invocationCount;

        public Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref invocationCount) == 1)
            {
                throw new InvalidOperationException("Expected test failure.");
            }

            return Task.FromResult(new IpcResponse(1, request.RequestId, request.Type, request.Payload, null));
        }
    }

    private sealed class GatedHandler : IIpcRequestHandler
    {
        private readonly SemaphoreSlim releases = new(0, 9);
        private int startedCount;

        public TaskCompletionSource EightStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource NinthStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StartedCount => Volatile.Read(ref startedCount);

        public async Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken cancellationToken)
        {
            int count = Interlocked.Increment(ref startedCount);
            if (count == 8) EightStarted.TrySetResult();
            if (count == 9) NinthStarted.TrySetResult();
            await releases.WaitAsync(cancellationToken);
            return new IpcResponse(1, request.RequestId, request.Type, request.Payload, null);
        }

        public void ReleaseOne() => releases.Release();
        public void ReleaseAll() => releases.Release(8);
    }
}
