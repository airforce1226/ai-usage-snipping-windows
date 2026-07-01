using System.Text.Json;
using AIUsageMonitor.Core.Abstractions;
using AIUsageMonitor.Core.Domain;
using Microsoft.Data.Sqlite;

namespace AIUsageMonitor.Infrastructure.Persistence;

public sealed class SqliteUsageEventStore : IUsageEventStore, ISourceCheckpointStore
{
    private readonly DatabaseConnectionFactory connectionFactory;

    public SqliteUsageEventStore(DatabaseConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async ValueTask<int> UpsertBatchAsync(
        IReadOnlyList<UsageEvent> events,
        SourceCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(checkpoint);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(
            cancellationToken);
        var inserted = 0;

        foreach (var usageEvent in events)
        {
            await using var command = CreateEventCommand(connection, transaction, usageEvent);
            inserted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = CreateCheckpointCommand(connection, transaction, checkpoint))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    public async ValueTask<SourceCheckpoint?> GetAsync(
        string canonicalPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT canonical_path, byte_offset, file_length,
                   last_write_time_utc_ms, parser_version, provider_state_json
            FROM source_checkpoints
            WHERE canonical_path = $canonicalPath COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$canonicalPath", canonicalPath);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var state = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(5))
            ?? new Dictionary<string, string>();
        return new SourceCheckpoint(
            reader.GetString(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
            reader.GetString(4),
            state);
    }

    private static SqliteCommand CreateEventCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UsageEvent usageEvent)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO usage_events (
                stable_key, provider, session_id, message_id, project_id,
                occurred_at_utc_ms, model, input_tokens, output_tokens,
                cache_read_tokens, cache_write_tokens, source_path, source_byte_offset)
            VALUES (
                $stableKey, $provider, $sessionId, $messageId, $projectId,
                $occurredAt, $model, $inputTokens, $outputTokens,
                $cacheReadTokens, $cacheWriteTokens, $sourcePath, $sourceByteOffset)
            ON CONFLICT(stable_key) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$stableKey", usageEvent.StableKey);
        command.Parameters.AddWithValue("$provider", (int)usageEvent.Provider);
        command.Parameters.AddWithValue("$sessionId", usageEvent.SessionId);
        command.Parameters.AddWithValue("$messageId", (object?)usageEvent.MessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("$projectId", usageEvent.ProjectId);
        command.Parameters.AddWithValue("$occurredAt", usageEvent.Timestamp.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$model", usageEvent.Model);
        command.Parameters.AddWithValue("$inputTokens", usageEvent.Tokens.InputTokens);
        command.Parameters.AddWithValue("$outputTokens", usageEvent.Tokens.OutputTokens);
        command.Parameters.AddWithValue("$cacheReadTokens", usageEvent.Tokens.CacheReadTokens);
        command.Parameters.AddWithValue("$cacheWriteTokens", usageEvent.Tokens.CacheWriteTokens);
        command.Parameters.AddWithValue("$sourcePath", usageEvent.Source.Path);
        command.Parameters.AddWithValue("$sourceByteOffset", usageEvent.Source.ByteOffset);
        return command;
    }

    private static SqliteCommand CreateCheckpointCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SourceCheckpoint checkpoint)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO source_checkpoints (
                canonical_path, byte_offset, file_length, last_write_time_utc_ms,
                parser_version, provider_state_json)
            VALUES (
                $canonicalPath, $byteOffset, $fileLength, $lastWriteTime,
                $parserVersion, $providerState)
            ON CONFLICT(canonical_path) DO UPDATE SET
                byte_offset = excluded.byte_offset,
                file_length = excluded.file_length,
                last_write_time_utc_ms = excluded.last_write_time_utc_ms,
                parser_version = excluded.parser_version,
                provider_state_json = excluded.provider_state_json;
            """;
        command.Parameters.AddWithValue("$canonicalPath", checkpoint.CanonicalPath);
        command.Parameters.AddWithValue("$byteOffset", checkpoint.ByteOffset);
        command.Parameters.AddWithValue("$fileLength", checkpoint.FileLength);
        command.Parameters.AddWithValue(
            "$lastWriteTime",
            checkpoint.LastWriteTimeUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$parserVersion", checkpoint.ParserVersion);
        command.Parameters.AddWithValue(
            "$providerState",
            JsonSerializer.Serialize(checkpoint.ProviderState ?? new Dictionary<string, string>()));
        return command;
    }
}
