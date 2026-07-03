using AIUsageMonitor.Core.Queries;
using Microsoft.Data.Sqlite;

namespace AIUsageMonitor.Infrastructure.Queries;

public sealed class SqliteUsageQueryService : IUsageQueryService
{
    public SqliteUsageQueryService(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    public string ConnectionString { get; }

    public async ValueTask<UsageSummary> GetSummaryAsync(UsageQueryRange range, CancellationToken cancellationToken)
    {
        EnsureValid(range);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COALESCE(SUM(input_tokens), 0), COALESCE(SUM(output_tokens), 0),
                   COALESCE(SUM(cache_read_tokens), 0), COALESCE(SUM(cache_write_tokens), 0), COUNT(*)
            FROM usage_events
            WHERE occurred_at_utc_ms >= $fromUtc AND occurred_at_utc_ms < $toUtc;
            """;
        AddRange(command, range);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var values = new long[] { reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4) };
        await reader.DisposeAsync();
        return new UsageSummary(values[0], values[1], values[2], values[3], values[4], await GetDatabaseUpdatedAtUtcAsync(connection, cancellationToken));
    }

    public ValueTask<PagedUsageResult<ProjectUsage>> GetProjectsAsync(PagedUsageQuery query, CancellationToken cancellationToken) =>
        QueryBreakdownAsync(query, "project_id", static reader => new ProjectUsage(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5)), cancellationToken);

    public ValueTask<PagedUsageResult<ModelUsage>> GetModelsAsync(PagedUsageQuery query, CancellationToken cancellationToken) =>
        QueryBreakdownAsync(query, "model", static reader => new ModelUsage(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5)), cancellationToken);

    public async ValueTask<PagedUsageResult<SessionUsage>> GetSessionsAsync(PagedUsageQuery query, CancellationToken cancellationToken)
    {
        EnsureValid(query);
        await using var connection = await OpenAsync(cancellationToken);
        var totalCount = await CountGroupsAsync(connection, "session_id", query.Range, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH filtered AS (
                SELECT * FROM usage_events
                WHERE occurred_at_utc_ms >= $fromUtc AND occurred_at_utc_ms < $toUtc
            ), aggregated AS (
                SELECT session_id, SUM(input_tokens) AS input_tokens, SUM(output_tokens) AS output_tokens,
                       SUM(cache_read_tokens) AS cache_read_tokens, SUM(cache_write_tokens) AS cache_write_tokens,
                       COUNT(*) AS event_count, MAX(occurred_at_utc_ms) AS last_event
                FROM filtered GROUP BY session_id
            ), latest_project AS (
                SELECT session_id, project_id,
                       ROW_NUMBER() OVER (
                           PARTITION BY session_id
                           ORDER BY occurred_at_utc_ms DESC, stable_key DESC) AS row_number
                FROM filtered
            )
            SELECT aggregated.session_id, latest_project.project_id, aggregated.input_tokens,
                   aggregated.output_tokens, aggregated.cache_read_tokens, aggregated.cache_write_tokens,
                   aggregated.event_count, aggregated.last_event
            FROM aggregated
            JOIN latest_project ON latest_project.session_id = aggregated.session_id
                               AND latest_project.row_number = 1
            ORDER BY aggregated.last_event DESC, aggregated.session_id ASC
            LIMIT $limit OFFSET $offset;
            """;
        AddQuery(command, query);
        var items = new List<SessionUsage>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new SessionUsage(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7))));
            }
        }

        return new(items, query.Page.Offset, query.Page.Limit, totalCount, await GetDatabaseUpdatedAtUtcAsync(connection, cancellationToken));
    }

    private async ValueTask<PagedUsageResult<T>> QueryBreakdownAsync<T>(PagedUsageQuery query, string column, Func<SqliteDataReader, T> map, CancellationToken cancellationToken)
    {
        EnsureValid(query);
        await using var connection = await OpenAsync(cancellationToken);
        var totalCount = await CountGroupsAsync(connection, column, query.Range, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {column}, SUM(input_tokens), SUM(output_tokens), SUM(cache_read_tokens), SUM(cache_write_tokens), COUNT(*)
            FROM usage_events WHERE occurred_at_utc_ms >= $fromUtc AND occurred_at_utc_ms < $toUtc
            GROUP BY {column}
            ORDER BY SUM(input_tokens + output_tokens + cache_read_tokens + cache_write_tokens) DESC, {column} ASC
            LIMIT $limit OFFSET $offset;
            """;
        AddQuery(command, query);
        var items = new List<T>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) items.Add(map(reader));
        }

        return new(items, query.Page.Offset, query.Page.Limit, totalCount, await GetDatabaseUpdatedAtUtcAsync(connection, cancellationToken));
    }

    private async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async ValueTask<long> CountGroupsAsync(SqliteConnection connection, string columns, UsageQueryRange range, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM (SELECT 1 FROM usage_events WHERE occurred_at_utc_ms >= $fromUtc AND occurred_at_utc_ms < $toUtc GROUP BY {columns});";
        AddRange(command, range);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async ValueTask<DateTimeOffset> GetDatabaseUpdatedAtUtcAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MAX(value) FROM (
                SELECT MAX(occurred_at_utc_ms) AS value FROM usage_events
                UNION ALL SELECT MAX(last_write_time_utc_ms) FROM source_checkpoints);
            """;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? DateTimeOffset.UnixEpoch : DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void AddRange(SqliteCommand command, UsageQueryRange range)
    {
        command.Parameters.AddWithValue("$fromUtc", range.FromUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$toUtc", range.ToUtc.ToUnixTimeMilliseconds());
    }

    private static void AddQuery(SqliteCommand command, PagedUsageQuery query)
    {
        AddRange(command, query.Range);
        command.Parameters.AddWithValue("$limit", query.Page.Limit);
        command.Parameters.AddWithValue("$offset", query.Page.Offset);
    }

    private static void EnsureValid(UsageQueryRange range)
    {
        if (!range.Validate().IsValid) throw new ArgumentException("Invalid usage query range.", nameof(range));
    }

    private static void EnsureValid(PagedUsageQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureValid(query.Range);
        if (!query.Page.Validate().IsValid) throw new ArgumentException("Invalid usage query page.", nameof(query));
    }
}
