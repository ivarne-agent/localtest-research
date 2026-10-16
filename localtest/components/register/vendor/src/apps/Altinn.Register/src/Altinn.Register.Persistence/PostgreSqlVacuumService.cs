using System.Text;
using Altinn.Authorization.ServiceDefaults.Npgsql;
using Altinn.Register.Core;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Altinn.Register.Persistence;

/// <summary>
/// Service for performing VACUUM operations on a PostgreSQL database.
/// </summary>
internal sealed partial class PostgreSqlVacuumService
{
    private readonly NpgsqlDataSource _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PostgreSqlVacuumService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlVacuumService"/> class.
    /// </summary>
    public PostgreSqlVacuumService(
        NpgsqlDataSource db,
        TimeProvider timeProvider,
        ILogger<PostgreSqlVacuumService> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Vacuums a table.
    /// </summary>
    /// <param name="schemaName">The table schema.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="options">Vacuum options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    public async Task VacuumTable(
        string? schemaName,
        string tableName,
        PostgreSqlVacuumSettings options,
        CancellationToken cancellationToken)
    {
        Guard.IsNotNullOrEmpty(tableName);

        var query = Query(options, (schemaName, tableName));
        var tableDisplayName = schemaName is null ? tableName : $"{schemaName}.{tableName}";
        using var activity = RegisterTelemetry.StartActivity($"vacuum {tableDisplayName}", tags: options.Tags);
        var start = _timeProvider.GetUtcNow();

        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand(query);

        Log.StartingVacuum(_logger, tableDisplayName);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        var duration = _timeProvider.GetUtcNow() - start;
        Log.FinishedVacuum(_logger, tableDisplayName, duration);
    }

    private static string Query(
        PostgreSqlVacuumSettings options,
        (string? Schema, string Table)? table)
    {
        var builder = new StringBuilder("VACUUM");
        if (options.Any())
        {
            var first = true;

            builder.Append(" (");
            BoolValue(builder, ref first, "FULL", options.Full);
            BoolValue(builder, ref first, "FREEZE", options.Freeze);
            BoolValue(builder, ref first, "ANALYZE", options.Analyze);
            BoolValue(builder, ref first, "DISABLE_PAGE_SKIPPING", options.DisablePageSkipping);
            BoolValue(builder, ref first, "SKIP_LOCKED", options.SkipLocked);
            LazyValue(builder, ref first, "INDEX_CLEANUP", options.IndexCleanup, static value => value switch
            {
                PostgreSqlVacuumIndexCleanup.Off => "OFF",
                PostgreSqlVacuumIndexCleanup.Auto => "AUTO",
                PostgreSqlVacuumIndexCleanup.On => "ON",
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unexpected value"),
            });
            BoolValue(builder, ref first, "TRUNCATE", options.Truncate);
            builder.Append(")");
        }

        if (table is { Schema: var schema, Table: var tbl })
        {
            builder.Append(' ');

            if (schema is not null)
            {
                builder.Append($"\"{schema}\".");
            }

            builder.Append($"\"{tbl}\"");
        }

        return builder.ToString();

        static void BoolValue(StringBuilder builder, ref bool first, string name, bool? optValue)
        {
            if (optValue is { } value)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    builder.Append(", ");
                }

                builder.Append(name).Append(' ').Append(value ? "TRUE" : "FALSE");
            }
        }

        static void LazyValue<T>(StringBuilder builder, ref bool first, string name, T? optValue, Func<T, string> toSql)
        {
            if (optValue is { } value)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    builder.Append(", ");
                }

                builder.Append(name).Append(' ').Append(toSql(value));
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "Starting VACUUM on table {Table}")]
        public static partial void StartingVacuum(ILogger logger, string table);

        [LoggerMessage(1, LogLevel.Information, "Finished VACUUM on table {Table} in {Duration}")]
        public static partial void FinishedVacuum(ILogger logger, string table, TimeSpan duration);
    }
}
