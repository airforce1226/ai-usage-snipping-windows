namespace AIUsageMonitor.App.ViewModels;

public interface IUsagePageViewModel
{
    bool IsLoading { get; }
    bool IsStale { get; }
    bool IsEmpty { get; }
    string? ErrorMessage { get; }
    DateTimeOffset? DataUpdatedAt { get; }
    Task LoadAsync(CancellationToken cancellationToken);
}

public abstract class UsagePageViewModel : ObservableObject, IUsagePageViewModel
{
    private bool isLoading;
    private bool isStale;
    private bool isEmpty;
    private string? errorMessage;
    private DateTimeOffset? dataUpdatedAt;

    public bool IsLoading { get => isLoading; protected set => SetProperty(ref isLoading, value); }
    public bool IsStale { get => isStale; protected set => SetProperty(ref isStale, value); }
    public bool IsEmpty { get => isEmpty; protected set => SetProperty(ref isEmpty, value); }
    public string? ErrorMessage { get => errorMessage; protected set => SetProperty(ref errorMessage, value); }
    public DateTimeOffset? DataUpdatedAt { get => dataUpdatedAt; protected set => SetProperty(ref dataUpdatedAt, value); }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            IsStale = false;
            ErrorMessage = null;
        }
        catch (Exception exception) when (IsOffline(exception, cancellationToken))
        {
            IsStale = DataUpdatedAt is not null;
            ErrorMessage = IsStale
                ? "Agent is offline. Showing the last loaded data."
                : "Agent is offline. Retry when it is available.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected abstract Task LoadCoreAsync(CancellationToken cancellationToken);

    private static bool IsOffline(Exception exception, CancellationToken cancellationToken) =>
        exception is TimeoutException or IOException or UnauthorizedAccessException
        || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
}
