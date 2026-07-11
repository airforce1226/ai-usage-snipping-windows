using AIUsageMonitor.App.Services;
using AIUsageMonitor.App.ViewModels;
using AIUsageMonitor.Core.Queries;
using AIUsageMonitor.Ipc.Client;
using AIUsageMonitor.Ipc.Security;
using AIUsageMonitor.Ipc.Transport;
using Microsoft.UI.Xaml.Controls;

namespace AIUsageMonitor.App;

public sealed class AppComposition
{
    private AppComposition(SummaryViewModel summary, ProjectsViewModel projects, ModelsViewModel models,
        SessionsViewModel sessions, SettingsViewModel settings, MainViewModel main)
    {
        Summary = summary; Projects = projects; Models = models; Sessions = sessions; Settings = settings; Main = main;
    }

    public SummaryViewModel Summary { get; }
    public ProjectsViewModel Projects { get; }
    public ModelsViewModel Models { get; }
    public SessionsViewModel Sessions { get; }
    public SettingsViewModel Settings { get; }
    public MainViewModel Main { get; }

    public static AppComposition Create()
    {
        string sid = new WindowsCurrentUserIdentity().GetSid();
        string pipe = UserEndpointIdentity.Create(sid, 1).PipeName;
        var agent = new AgentUsageQueryClient(new NamedPipeAgentClient(pipe), "default", TimeSpan.FromMilliseconds(750));
        Func<UsageQueryRange> range = CurrentLocalMonth;
        var summary = new SummaryViewModel(agent, range);
        var projects = new ProjectsViewModel(agent, range);
        var models = new ModelsViewModel(agent, range);
        var sessions = new SessionsViewModel(agent, range);
        var settings = new SettingsViewModel(new StartupTaskService(new WindowsStartupTaskBridge()));
        var pages = new Dictionary<PageKind, IUsagePageViewModel>
        {
            [PageKind.Summary] = summary, [PageKind.Projects] = projects,
            [PageKind.Models] = models, [PageKind.Sessions] = sessions,
        };
        return new(summary, projects, models, sessions, settings, new MainViewModel(pages, agent));
    }

    public Page CreatePage(PageKind page) => page switch
    {
        PageKind.Projects => DashboardPages.Paged("Projects", Projects),
        PageKind.Models => DashboardPages.Paged("Models", Models),
        PageKind.Sessions => DashboardPages.Paged("Sessions", Sessions),
        PageKind.Settings => DashboardPages.Settings(Settings),
        _ => DashboardPages.Summary(Summary),
    };

    private static UsageQueryRange CurrentLocalMonth()
    {
        DateTime now = DateTime.Now;
        var from = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Local);
        return new(new DateTimeOffset(from.ToUniversalTime(), TimeSpan.Zero),
            new DateTimeOffset(from.AddMonths(1).ToUniversalTime(), TimeSpan.Zero));
    }
}
