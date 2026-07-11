using AIUsageMonitor.Cli;

namespace AIUsageMonitor.Cli.Tests;

public sealed class CliCompositionTests
{
    [Fact]
    public void GetDefaultDatabasePath_UsesLocalApplicationDataProfileDatabase()
    {
        string root = Path.Combine("C:\\", "Users", "test", "AppData", "Local");
        Assert.Equal(Path.Combine(root, "AIUsageMonitor", "profiles", "default", "usage.db"),
            CliComposition.GetDefaultDatabasePath(root));
    }

    [Fact]
    public void Create_BuildsApplicationForExplicitRuntimeValues()
    {
        CliApplication application = CliComposition.Create(
            "S-1-5-21-test", Path.Combine(Path.GetTempPath(), "usage.db"),
            TextWriter.Null, TextWriter.Null, TimeProvider.System, TimeZoneInfo.Local);
        Assert.NotNull(application);
    }
}
