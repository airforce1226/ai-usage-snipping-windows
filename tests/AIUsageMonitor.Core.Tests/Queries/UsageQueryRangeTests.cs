using AIUsageMonitor.Core.Queries;

namespace AIUsageMonitor.Core.Tests.Queries;

public sealed class UsageQueryRangeTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, -5)]
    public void Validate_RejectsNonUtcTimestamp(int fromOffsetHours, int toOffsetHours)
    {
        var range = new UsageQueryRange(
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(fromOffsetHours)),
            new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.FromHours(toOffsetHours)));

        Assert.Equal(UsageQueryValidationErrorCodes.NonUtcTimestamp, range.Validate().ErrorCode);
    }
}
