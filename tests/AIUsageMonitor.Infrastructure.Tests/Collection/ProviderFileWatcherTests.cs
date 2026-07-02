using AIUsageMonitor.Infrastructure.Collection;

namespace AIUsageMonitor.Infrastructure.Tests.Collection;

public sealed class ProviderFileWatcherTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"AIUsageMonitor-watcher-{Guid.NewGuid():N}");

    public ProviderFileWatcherTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task DisposeAsync_AwaitsInflightCallback()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new ProviderFileWatcher(root, async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        }, () => ValueTask.CompletedTask);
        watcher.Start();
        await File.WriteAllTextAsync(Path.Combine(root, "event.jsonl"), "{}\n");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposal = watcher.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        release.TrySetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
