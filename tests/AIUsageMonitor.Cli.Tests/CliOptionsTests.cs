using AIUsageMonitor.Cli;

namespace AIUsageMonitor.Cli.Tests;

public sealed class CliOptionsTests
{
    private static readonly TimeZoneInfo Korea = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
    private static readonly TimeProvider Clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-07-18T05:00:00Z"));

    [Theory]
    [InlineData("summary", CliCommand.Summary)]
    [InlineData("projects", CliCommand.Projects)]
    [InlineData("models", CliCommand.Models)]
    [InlineData("sessions", CliCommand.Sessions)]
    [InlineData("refresh", CliCommand.Refresh)]
    public void Parse_RecognizesCommands(string value, CliCommand expected)
    {
        CliParseResult result = CliOptions.Parse([value], Clock, Korea);
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Options!.Command);
    }

    [Fact]
    public void Parse_DefaultsToCurrentLocalMonthAndFiftyRows()
    {
        CliParseResult result = CliOptions.Parse(["summary"], Clock, Korea);
        Assert.Equal(DateTimeOffset.Parse("2026-06-30T15:00:00Z"), result.Options!.Range.FromUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-07-31T15:00:00Z"), result.Options.Range.ToUtc);
        Assert.Equal(50, result.Options.Page.Limit);
    }

    [Fact]
    public void Parse_AcceptsRangePagingAndJson()
    {
        CliParseResult result = CliOptions.Parse(
            ["projects", "--from", "2026-07-01", "--to", "2026-08-01", "--offset", "10", "--limit", "25", "--json"], Clock, Korea);
        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Options!.Page.Offset);
        Assert.Equal(25, result.Options.Page.Limit);
        Assert.True(result.Options.Json);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("summary --from bad")]
    [InlineData("summary --from 2026-08-01 --to 2026-07-01")]
    [InlineData("projects --offset -1")]
    [InlineData("projects --limit 201")]
    [InlineData("refresh --json")]
    [InlineData("summary --limit 10")]
    [InlineData("projects --limit 10 --limit 20")]
    public void Parse_RejectsInvalidArguments(string commandLine)
    {
        CliParseResult result = CliOptions.Parse(commandLine.Split(' '), Clock, Korea);
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
