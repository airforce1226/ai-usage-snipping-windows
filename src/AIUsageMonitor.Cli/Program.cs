using AIUsageMonitor.Cli;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

Environment.ExitCode = await CliComposition.CreateDefault().RunAsync(args, cancellation.Token);
