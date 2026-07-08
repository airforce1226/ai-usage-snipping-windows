using System.Xml.Linq;

namespace AIUsageMonitor.App.Tests.Packaging;

public sealed class PackageManifestTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ManifestDeclaresVisibleAppAndOptInAgentStartupTask()
    {
        XDocument manifest = XDocument.Load(Path.Combine(RepositoryRoot, "packaging", "AIUsageMonitor.Package", "Package.appxmanifest"));
        XNamespace foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
        XNamespace restrictedCapabilities = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

        XElement identity = Assert.Single(manifest.Descendants(foundation + "Identity"));
        XElement targetDeviceFamily = Assert.Single(manifest.Descendants(foundation + "TargetDeviceFamily"));
        XElement application = Assert.Single(manifest.Descendants(foundation + "Application"));
        XElement startupTask = Assert.Single(manifest.Descendants(desktop + "StartupTask"));
        XElement startupExtension = Assert.IsType<XElement>(startupTask.Parent);

        Assert.False(string.IsNullOrWhiteSpace(identity.Attribute("Publisher")?.Value));
        Assert.Equal("10.0.22621.0", targetDeviceFamily.Attribute("MinVersion")?.Value);
        Assert.EndsWith("AIUsageMonitor.App.exe", application.Attribute("Executable")?.Value, StringComparison.Ordinal);
        Assert.EndsWith("AIUsageMonitor.Agent.exe", startupExtension.Attribute("Executable")?.Value, StringComparison.Ordinal);
        Assert.Equal("AIUsageMonitorAgentStartup", startupTask.Attribute("TaskId")?.Value);
        Assert.Equal("false", startupTask.Attribute("Enabled")?.Value);
        Assert.Equal("runFullTrust", Assert.Single(manifest.Descendants(restrictedCapabilities + "Capability")).Attribute("Name")?.Value);
    }

    [Fact]
    public void PackagingProjectIncludesAgentCliAndBothArchitectures()
    {
        XDocument project = XDocument.Load(Path.Combine(RepositoryRoot, "packaging", "AIUsageMonitor.Package", "AIUsageMonitor.Package.wapproj"));
        string xml = project.ToString(SaveOptions.DisableFormatting);

        Assert.Contains("AIUsageMonitor.Agent.csproj", xml, StringComparison.Ordinal);
        Assert.Contains("AIUsageMonitor.Cli.csproj", xml, StringComparison.Ordinal);
        Assert.Contains("x64", xml, StringComparison.Ordinal);
        Assert.Contains("ARM64", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void AllProjectsDeclareBothWindowsRuntimeIdentifiers()
    {
        XDocument project = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        string runtimeIdentifiers = project.Descendants("RuntimeIdentifiers").Single().Value;

        Assert.Contains("win-x64", runtimeIdentifiers, StringComparison.Ordinal);
        Assert.Contains("win-arm64", runtimeIdentifiers, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AIUsageMonitor.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
