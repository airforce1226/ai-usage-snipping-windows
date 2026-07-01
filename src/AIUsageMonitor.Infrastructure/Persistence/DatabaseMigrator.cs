using Microsoft.Data.Sqlite;

namespace AIUsageMonitor.Infrastructure.Persistence;

public sealed class DatabaseMigrator
{
    private readonly DatabaseConnectionFactory connectionFactory;

    public DatabaseMigrator(DatabaseConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(connectionFactory.DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);
        await ExecuteAsync(connection, "PRAGMA synchronous=NORMAL;", cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async ValueTask ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS schema_versions (
            version INTEGER NOT NULL PRIMARY KEY,
            applied_at_utc_ms INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS usage_events (
            stable_key TEXT NOT NULL PRIMARY KEY,
            provider INTEGER NOT NULL,
            session_id TEXT NOT NULL,
            message_id TEXT NULL,
            project_id TEXT NOT NULL,
            occurred_at_utc_ms INTEGER NOT NULL,
            model TEXT NOT NULL,
            input_tokens INTEGER NOT NULL CHECK (input_tokens >= 0),
            output_tokens INTEGER NOT NULL CHECK (output_tokens >= 0),
            cache_read_tokens INTEGER NOT NULL CHECK (cache_read_tokens >= 0),
            cache_write_tokens INTEGER NOT NULL CHECK (cache_write_tokens >= 0),
            source_path TEXT NOT NULL,
            source_byte_offset INTEGER NOT NULL CHECK (source_byte_offset >= 0)
        );

        CREATE INDEX IF NOT EXISTS ix_usage_events_occurred_at
            ON usage_events (occurred_at_utc_ms);
        CREATE INDEX IF NOT EXISTS ix_usage_events_project_model
            ON usage_events (project_id, model);

        CREATE TABLE IF NOT EXISTS source_checkpoints (
            canonical_path TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
            byte_offset INTEGER NOT NULL CHECK (byte_offset >= 0),
            file_length INTEGER NOT NULL CHECK (file_length >= 0),
            last_write_time_utc_ms INTEGER NOT NULL,
            parser_version TEXT NOT NULL,
            provider_state_json TEXT NOT NULL
        );

        INSERT OR IGNORE INTO schema_versions (version, applied_at_utc_ms)
        VALUES (1, unixepoch('subsec') * 1000);
        """;
}
