using System.Globalization;
using AIUsageMonitor.Core.Queries;

namespace AIUsageMonitor.Cli;

public enum CliCommand { Summary, Projects, Models, Sessions, Refresh }

public sealed record CliOptions(CliCommand Command, UsageQueryRange Range, UsagePage Page, bool Json)
{
    public static CliParseResult Parse(string[] args, TimeProvider timeProvider, TimeZoneInfo localZone)
    {
        if (args.Length == 0 || !TryCommand(args[0], out CliCommand command))
        {
            return Invalid("Expected one of: summary, projects, models, sessions, refresh.");
        }

        DateOnly? from = null;
        DateOnly? to = null;
        var offset = 0;
        var limit = 50;
        var json = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 1; index < args.Length; index++)
        {
            string option = args[index];
            if (!seen.Add(option)) return Invalid($"Duplicate option: {option}.");
            if (option == "--json") { json = true; continue; }
            if (option is not ("--from" or "--to" or "--offset" or "--limit") || ++index >= args.Length)
                return Invalid($"Invalid option or missing value: {option}.");
            string value = args[index];
            switch (option)
            {
                case "--from" when TryDate(value, out DateOnly date): from = date; break;
                case "--to" when TryDate(value, out DateOnly date): to = date; break;
                case "--offset" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number): offset = number; break;
                case "--limit" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number): limit = number; break;
                default: return Invalid($"Invalid value for {option}: {value}.");
            }
        }

        bool paged = command is CliCommand.Projects or CliCommand.Models or CliCommand.Sessions;
        if (command == CliCommand.Refresh && (seen.Count > 0 || json)) return Invalid("refresh does not accept options.");
        if (!paged && (seen.Contains("--offset") || seen.Contains("--limit"))) return Invalid("Paging is only valid for list commands.");
        if (offset < 0 || limit is < 1 or > 200) return Invalid("Paging requires offset >= 0 and limit between 1 and 200.");

        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), localZone);
        DateOnly defaultFrom = new(localNow.Year, localNow.Month, 1);
        DateOnly rangeFrom = from ?? defaultFrom;
        DateOnly rangeTo = to ?? defaultFrom.AddMonths(1);
        DateTimeOffset fromUtc = ToUtc(rangeFrom, localZone);
        DateTimeOffset toUtc = ToUtc(rangeTo, localZone);
        if (fromUtc >= toUtc) return Invalid("--from must be earlier than --to.");

        return new(new(command, new(fromUtc, toUtc), new(offset, limit), json), null);
    }

    private static bool TryCommand(string value, out CliCommand command) =>
        Enum.TryParse(value, true, out command) && value.Equals(command.ToString(), StringComparison.OrdinalIgnoreCase);

    private static bool TryDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static DateTimeOffset ToUtc(DateOnly date, TimeZoneInfo zone)
    {
        DateTime local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private static CliParseResult Invalid(string error) => new(null, error);
}

public sealed record CliParseResult(CliOptions? Options, string? Error)
{
    public bool IsSuccess => Options is not null;
}
