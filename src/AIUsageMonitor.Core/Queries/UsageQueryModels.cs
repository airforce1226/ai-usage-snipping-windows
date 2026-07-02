namespace AIUsageMonitor.Core.Queries;

public static class UsageQueryValidationErrorCodes
{
    public const string InvalidRange = "usage.invalid_range";
    public const string InvalidPageSize = "usage.invalid_page_size";
    public const string InvalidOffset = "usage.invalid_offset";
}

public sealed record QueryValidationResult(bool IsValid, string? ErrorCode)
{
    public static QueryValidationResult Valid { get; } = new(true, null);
    public static QueryValidationResult Invalid(string errorCode) => new(false, errorCode);
}

public sealed record UsageQueryRange(DateTimeOffset FromUtc, DateTimeOffset ToUtc)
{
    public QueryValidationResult Validate() => FromUtc >= ToUtc
        ? QueryValidationResult.Invalid(UsageQueryValidationErrorCodes.InvalidRange)
        : QueryValidationResult.Valid;
}

public sealed record UsagePage(int Offset, int Limit)
{
    public QueryValidationResult Validate()
    {
        if (Limit is < 1 or > 200)
        {
            return QueryValidationResult.Invalid(UsageQueryValidationErrorCodes.InvalidPageSize);
        }

        return Offset < 0
            ? QueryValidationResult.Invalid(UsageQueryValidationErrorCodes.InvalidOffset)
            : QueryValidationResult.Valid;
    }
}

public sealed record PagedUsageQuery(UsageQueryRange Range, UsagePage Page);

public sealed record UsageSummary(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    long EventCount,
    DateTimeOffset DatabaseUpdatedAtUtc);

public sealed record ProjectUsage(
    string ProjectId,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    long EventCount)
{
    public long TotalTokens => checked(InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens);
}

public sealed record ModelUsage(
    string Model,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    long EventCount)
{
    public long TotalTokens => checked(InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens);
}

public sealed record SessionUsage(
    string SessionId,
    string ProjectId,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    long EventCount,
    DateTimeOffset LastEventUtc)
{
    public long TotalTokens => checked(InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens);
}

public sealed record PagedUsageResult<T>(
    IReadOnlyList<T> Items,
    int Offset,
    int Limit,
    long TotalCount,
    DateTimeOffset DatabaseUpdatedAtUtc);
