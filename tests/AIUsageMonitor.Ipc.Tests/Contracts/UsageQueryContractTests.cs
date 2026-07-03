using System.Globalization;
using System.Text.Json;
using AIUsageMonitor.Ipc.Contracts;

namespace AIUsageMonitor.Ipc.Tests.Contracts;

public sealed class UsageQueryContractTests
{
    public static IEnumerable<object[]> Payloads()
    {
        var from = DateTimeOffset.Parse("2026-07-01T00:00:00.123Z", CultureInfo.InvariantCulture);
        var to = DateTimeOffset.Parse("2026-07-02T00:00:00.456Z", CultureInfo.InvariantCulture);
        var updated = DateTimeOffset.Parse("2026-07-02T01:02:03.789Z", CultureInfo.InvariantCulture);
        yield return [new UsageSummaryRequest("profile-a", from, to), typeof(UsageSummaryRequest)];
        yield return [new UsageProjectsRequest("profile-a", from, to, 4, 25), typeof(UsageProjectsRequest)];
        yield return [new UsageModelsRequest("profile-a", from, to, 2, 10), typeof(UsageModelsRequest)];
        yield return [new UsageSessionsRequest("profile-a", from, to, 0, 200), typeof(UsageSessionsRequest)];
        yield return [new UsageSummaryResponse(1, 2, 3, 4, 5, updated), typeof(UsageSummaryResponse)];
        yield return [new UsageProjectsResponse([new ProjectUsagePayload("p", 1, 2, 3, 4, 5)], 0, 10, 1, updated), typeof(UsageProjectsResponse)];
        yield return [new UsageModelsResponse([new ModelUsagePayload("m", 1, 2, 3, 4, 5)], 0, 10, 1, updated), typeof(UsageModelsResponse)];
        yield return [new UsageSessionsResponse([new SessionUsagePayload("s", "p", 1, 2, 3, 4, 5, updated)], 0, 10, 1, updated), typeof(UsageSessionsResponse)];
    }

    [Theory]
    [MemberData(nameof(Payloads))]
    public void Payload_RoundTripsWithCamelCaseAndInvariantUtc(object payload, Type payloadType)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-SA");
            var json = JsonSerializer.Serialize(payload, payloadType, UsageQueryJson.Options);
            var roundTrip = JsonSerializer.Deserialize(json, payloadType, UsageQueryJson.Options);

            Assert.NotNull(roundTrip);
            Assert.Equal(json, JsonSerializer.Serialize(roundTrip, payloadType, UsageQueryJson.Options));
            Assert.DoesNotContain("ProfileId", json, StringComparison.Ordinal);
            Assert.Contains("2026-07-", json, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Commands_AreProtocolStable()
    {
        Assert.Equal("usage.summary.get", UsageQueryCommands.SummaryGet);
        Assert.Equal("usage.projects.get", UsageQueryCommands.ProjectsGet);
        Assert.Equal("usage.models.get", UsageQueryCommands.ModelsGet);
        Assert.Equal("usage.sessions.get", UsageQueryCommands.SessionsGet);
    }

    [Theory]
    [InlineData("2026-07-01T00:00:00Z", "2026-07-01T00:00:00Z")]
    [InlineData("2026-07-02T00:00:00Z", "2026-07-01T00:00:00Z")]
    public void Request_RejectsNonIncreasingRange(string fromText, string toText)
    {
        var request = new UsageSummaryRequest("profile", DateTimeOffset.Parse(fromText), DateTimeOffset.Parse(toText));

        Assert.Equal(UsageQueryValidationErrorCodes.InvalidRange, request.Validate().ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void PagedRequest_RejectsInvalidPageSize(int limit)
    {
        var request = new UsageProjectsRequest("profile", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), 0, limit);

        Assert.Equal(UsageQueryValidationErrorCodes.InvalidPageSize, request.Validate().ErrorCode);
    }

    [Fact]
    public void Request_RejectsNonUtcTimestamp()
    {
        var request = new UsageSummaryRequest(
            "profile",
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(UsageQueryValidationErrorCodes.NonUtcTimestamp, request.Validate().ErrorCode);
    }
}
