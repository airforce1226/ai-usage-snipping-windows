using System.Text.Json;

namespace AIUsageMonitor.Ipc.Contracts;

public static class UsageQueryCommands
{
    public const string SummaryGet = "usage.summary.get";
    public const string ProjectsGet = "usage.projects.get";
    public const string ModelsGet = "usage.models.get";
    public const string SessionsGet = "usage.sessions.get";
}

public static class UsageQueryValidationErrorCodes
{
    public const string InvalidRange = "usage.invalid_range";
    public const string InvalidPageSize = "usage.invalid_page_size";
    public const string InvalidOffset = "usage.invalid_offset";
    public const string InvalidProfileId = "usage.invalid_profile_id";
}

public sealed record UsageQueryValidationResult(bool IsValid, string? ErrorCode)
{
    internal static UsageQueryValidationResult Validate(string profileId, DateTimeOffset fromUtc, DateTimeOffset toUtc, int? offset = null, int? limit = null)
    {
        if (string.IsNullOrWhiteSpace(profileId)) return new(false, UsageQueryValidationErrorCodes.InvalidProfileId);
        if (fromUtc >= toUtc) return new(false, UsageQueryValidationErrorCodes.InvalidRange);
        if (limit is < 1 or > 200) return new(false, UsageQueryValidationErrorCodes.InvalidPageSize);
        if (offset < 0) return new(false, UsageQueryValidationErrorCodes.InvalidOffset);
        return new(true, null);
    }
}

public sealed record UsageSummaryRequest(string ProfileId, DateTimeOffset FromUtc, DateTimeOffset ToUtc)
{
    public UsageQueryValidationResult Validate() => UsageQueryValidationResult.Validate(ProfileId, FromUtc, ToUtc);
}

public sealed record UsageProjectsRequest(string ProfileId, DateTimeOffset FromUtc, DateTimeOffset ToUtc, int Offset, int Limit)
{
    public UsageQueryValidationResult Validate() => UsageQueryValidationResult.Validate(ProfileId, FromUtc, ToUtc, Offset, Limit);
}

public sealed record UsageModelsRequest(string ProfileId, DateTimeOffset FromUtc, DateTimeOffset ToUtc, int Offset, int Limit)
{
    public UsageQueryValidationResult Validate() => UsageQueryValidationResult.Validate(ProfileId, FromUtc, ToUtc, Offset, Limit);
}

public sealed record UsageSessionsRequest(string ProfileId, DateTimeOffset FromUtc, DateTimeOffset ToUtc, int Offset, int Limit)
{
    public UsageQueryValidationResult Validate() => UsageQueryValidationResult.Validate(ProfileId, FromUtc, ToUtc, Offset, Limit);
}

public sealed record UsageSummaryResponse(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, long EventCount, DateTimeOffset DatabaseUpdatedAtUtc);
public sealed record ProjectUsagePayload(string ProjectId, long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, long EventCount);
public sealed record ModelUsagePayload(string Model, long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, long EventCount);
public sealed record SessionUsagePayload(string SessionId, string ProjectId, long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, long EventCount, DateTimeOffset LastEventUtc);
public sealed record UsageProjectsResponse(IReadOnlyList<ProjectUsagePayload> Items, int Offset, int Limit, long TotalCount, DateTimeOffset DatabaseUpdatedAtUtc);
public sealed record UsageModelsResponse(IReadOnlyList<ModelUsagePayload> Items, int Offset, int Limit, long TotalCount, DateTimeOffset DatabaseUpdatedAtUtc);
public sealed record UsageSessionsResponse(IReadOnlyList<SessionUsagePayload> Items, int Offset, int Limit, long TotalCount, DateTimeOffset DatabaseUpdatedAtUtc);

public static class UsageQueryJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
