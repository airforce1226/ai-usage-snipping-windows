using AIUsageMonitor.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace AIUsageMonitor.App;

internal static class DashboardPages
{
    public static Page Summary(SummaryViewModel viewModel)
    {
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(24) };
        panel.Children.Add(Title("This month"));
        panel.Children.Add(Card("Input tokens", viewModel, nameof(viewModel.InputTokens)));
        panel.Children.Add(Card("Output tokens", viewModel, nameof(viewModel.OutputTokens)));
        panel.Children.Add(Card("Cache read", viewModel, nameof(viewModel.CacheReadTokens)));
        panel.Children.Add(Card("Cache write", viewModel, nameof(viewModel.CacheWriteTokens)));
        panel.Children.Add(Card("Events", viewModel, nameof(viewModel.EventCount)));
        return new Page { Content = new ScrollViewer { Content = panel } };
    }

    public static Page Paged<T>(string heading, PagedUsageViewModel<T> viewModel)
    {
        var list = new ListView();
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding { Source = viewModel, Path = new PropertyPath(nameof(viewModel.Items)), Mode = BindingMode.OneWay });
        var previous = new Button { Content = "Previous" };
        var next = new Button { Content = "Next" };
        previous.Click += async (_, _) => await viewModel.PreviousAsync(CancellationToken.None);
        next.Click += async (_, _) => await viewModel.NextAsync(CancellationToken.None);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(previous); buttons.Children.Add(next);
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(24) };
        panel.Children.Add(Title(heading)); panel.Children.Add(list); panel.Children.Add(buttons);
        return new Page { Content = panel };
    }

    public static Page Settings(SettingsViewModel viewModel)
    {
        var toggle = new ToggleSwitch { Header = "Start Agent when I sign in" };
        toggle.SetBinding(ToggleSwitch.IsOnProperty, new Binding { Source = viewModel, Path = new PropertyPath(nameof(viewModel.IsEnabled)), Mode = BindingMode.OneWay });
        toggle.Toggled += async (_, _) =>
        {
            if (toggle.IsOn != viewModel.IsEnabled) await viewModel.SetEnabledAsync(toggle.IsOn, CancellationToken.None);
        };
        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(24) };
        panel.Children.Add(Title("Settings")); panel.Children.Add(toggle);
        return new Page { Content = panel };
    }

    private static TextBlock Title(string text) => new() { Text = text, Style = Application.Current.Resources["TitleTextBlockStyle"] as Style };

    private static Border Card(string label, object source, string path)
    {
        var value = new TextBlock { FontSize = 28 };
        value.SetBinding(TextBlock.TextProperty, new Binding { Source = source, Path = new PropertyPath(path), Mode = BindingMode.OneWay });
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(new TextBlock { Text = label, Opacity = 0.7 }); content.Children.Add(value);
        return new Border { Child = content, Padding = new Thickness(16), CornerRadius = new CornerRadius(8), Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush };
    }
}
