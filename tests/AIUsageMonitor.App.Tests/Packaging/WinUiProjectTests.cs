using System.Xml.Linq;

namespace AIUsageMonitor.App.Tests.Packaging;

public sealed class WinUiProjectTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void AppProject_UsesWinUiWithoutInfrastructureReference()
    {
        XDocument project = XDocument.Load(Path.Combine(Root, "src", "AIUsageMonitor.App", "AIUsageMonitor.App.csproj"));
        string xml = project.ToString(SaveOptions.DisableFormatting);
        Assert.Contains("<UseWinUI>true</UseWinUI>", xml);
        Assert.Contains("Microsoft.WindowsAppSDK", xml);
        Assert.DoesNotContain("AIUsageMonitor.Infrastructure", xml);
    }

    [Fact]
    public void MainWindow_DeclaresAllNavigationDestinations()
    {
        string xaml = File.ReadAllText(Path.Combine(Root, "src", "AIUsageMonitor.App", "MainWindow.xaml"));
        foreach (string tag in new[] { "summary", "projects", "models", "sessions", "settings" })
            Assert.Contains($"Tag=\"{tag}\"", xaml, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AIUsageMonitor.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
