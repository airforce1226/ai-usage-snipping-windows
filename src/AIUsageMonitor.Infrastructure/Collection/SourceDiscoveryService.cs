namespace AIUsageMonitor.Infrastructure.Collection;

public class SourceDiscoveryService
{
    public virtual IEnumerable<string> Discover(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)
            .Select(Path.GetFullPath);
    }
}
