using AIUsageMonitor.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AIUsageMonitor.App;

public sealed partial class MainWindow : Window
{
    private readonly AppComposition composition;
    public MainWindow()
    {
        InitializeComponent();
        composition = AppComposition.Create();
        Closed += (_, _) => composition.Main.Dispose();
    }

    private void OnLoaded(object sender, RoutedEventArgs args) => Navigation.SelectedItem = Navigation.MenuItems[0];

    private async void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag) return;
        PageKind page = tag switch { "projects" => PageKind.Projects, "models" => PageKind.Models,
            "sessions" => PageKind.Sessions, "settings" => PageKind.Settings, _ => PageKind.Summary };
        ContentFrame.Content = composition.CreatePage(page);
        if (page == PageKind.Settings) await composition.Settings.LoadAsync(CancellationToken.None);
        else await composition.Main.NavigateAsync(page);
        StatusText.Text = composition.Main.ActivePage?.IsStale == true ? "Offline · showing cached data" : "Connected";
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs args)
    {
        try { await composition.Main.RefreshAsync(); StatusText.Text = "Data refreshed"; }
        catch { StatusText.Text = "Agent is offline"; }
    }
}
