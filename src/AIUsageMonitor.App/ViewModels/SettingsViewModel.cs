using AIUsageMonitor.App.Services;

namespace AIUsageMonitor.App.ViewModels;

public sealed class SettingsViewModel(StartupTaskService startupTaskService) : ObservableObject
{
    private StartupTaskState state;
    private bool isLoading;

    public StartupTaskState State { get => state; private set { if (SetProperty(ref state, value)) Notify(nameof(IsEnabled)); } }
    public bool IsEnabled => State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    public bool IsLoading { get => isLoading; private set => SetProperty(ref isLoading, value); }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try { State = await startupTaskService.GetStateAsync(cancellationToken).ConfigureAwait(false); }
        finally { IsLoading = false; }
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        IsLoading = true;
        try { State = await startupTaskService.SetEnabledAsync(enabled, cancellationToken).ConfigureAwait(false); }
        finally { IsLoading = false; }
    }
}
