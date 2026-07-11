using AIUsageMonitor.Core.Queries;
using AIUsageMonitor.Ipc.Client;

namespace AIUsageMonitor.Cli;

public sealed class CliApplication(
    IUsageQueryService queries,
    ICollectionRefreshClient refresh,
    TextWriter output,
    TextWriter error,
    TimeProvider timeProvider,
    TimeZoneInfo localZone)
{
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        CliParseResult parsed = CliOptions.Parse(args, timeProvider, localZone);
        if (!parsed.IsSuccess)
        {
            await error.WriteLineAsync(parsed.Error);
            await error.WriteLineAsync("Usage: ai-usage-status <summary|projects|models|sessions|refresh> [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--offset n] [--limit n] [--json]");
            return 2;
        }

        CliOptions options = parsed.Options!;
        try
        {
            switch (options.Command)
            {
                case CliCommand.Summary:
                    await UsageOutputWriter.WriteSummaryAsync(output, await queries.GetSummaryAsync(options.Range, cancellationToken), options.Json);
                    break;
                case CliCommand.Projects:
                    await UsageOutputWriter.WriteProjectsAsync(output, await queries.GetProjectsAsync(new(options.Range, options.Page), cancellationToken), options.Json);
                    break;
                case CliCommand.Models:
                    await UsageOutputWriter.WriteModelsAsync(output, await queries.GetModelsAsync(new(options.Range, options.Page), cancellationToken), options.Json);
                    break;
                case CliCommand.Sessions:
                    await UsageOutputWriter.WriteSessionsAsync(output, await queries.GetSessionsAsync(new(options.Range, options.Page), cancellationToken), options.Json);
                    break;
                case CliCommand.Refresh:
                    await refresh.RefreshAsync(cancellationToken);
                    await output.WriteLineAsync("refresh_accepted");
                    break;
            }
            return 0;
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            await error.WriteLineAsync("agent_unavailable");
            return 5;
        }
        catch (Exception exception)
        {
            await error.WriteLineAsync($"unexpected_error: {exception.Message}");
            return 1;
        }
    }

    private static bool IsUnavailable(Exception exception, CancellationToken cancellationToken) =>
        exception is TimeoutException or IOException or UnauthorizedAccessException
        || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
}
