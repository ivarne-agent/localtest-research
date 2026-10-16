using System.Diagnostics;
using Altinn.Register.Core;
using Altinn.Register.Core.Utils;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Altinn.Register.Persistence;

/// <summary>
/// Service for checking the size of tables in PostgreSQL.
/// </summary>
internal sealed partial class PostgreSqlSizeService
{
    private readonly Lock _lock = new();
    private Task<Cache>? _cacheTask;
    private Cache? _cache;

    private readonly Func<Task> _refreshCacheAction;

    private readonly NpgsqlDataSource _db;
    private readonly TimeProvider _timeProvider;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<PostgreSqlSizeService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlSizeService"/> class.
    /// </summary>
    public PostgreSqlSizeService(
        NpgsqlDataSource db,
        TimeProvider timeProvider,
        IHostApplicationLifetime lifetime,
        ILogger<PostgreSqlSizeService> logger)
    {
        Guard.IsNotNull(db);
        Guard.IsNotNull(timeProvider);
        Guard.IsNotNull(logger);

        _db = db;
        _timeProvider = timeProvider;
        _lifetime = lifetime;
        _logger = logger;

        _refreshCacheAction = RefreshCacheAsync;
    }

    /// <summary>
    /// Checks if the size of the database exceeds a specified threshold.
    /// </summary>
    /// <param name="sizeThreshold">The size threshold.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> if the database size is smaller than <paramref name="sizeThreshold"/>, otherwise <see langword="false"/>.</returns>
    public ValueTask<bool> IsDatabaseSmallerThan(
        ByteSize sizeThreshold,
        CancellationToken cancellationToken = default)
        => WithCache(
            sizeThreshold,
            static (self, cache, sizeThreshold) =>
            {
                if (cache.TotalSize >= sizeThreshold)
                {
                    Log.DatabaseSizeExceedsTheshold(self._logger, cache.TotalSize, sizeThreshold);
                    return false;
                }

                return true;
            },
            cancellationToken);

    /// <summary>
    /// Checks if the size of any table exceeds a specified threshold.
    /// </summary>
    /// <param name="sizeThreshold">The size threshold.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> if any table size is smaller than <paramref name="sizeThreshold"/>, otherwise <see langword="false"/>.</returns>
    public ValueTask<bool> IsAnyTableSmallerThan(
        ByteSize sizeThreshold,
        CancellationToken cancellationToken = default)
        => WithCache(
            sizeThreshold,
            static (self, cache, sizeThreshold) =>
            {
                var (schema, table, size) = cache.BiggestTable;
                if (size >= sizeThreshold)
                {
                    Log.TableSizeExceedsThreshold(self._logger, schema, table, size, sizeThreshold);
                    return false;
                }

                return true;
            },
            cancellationToken);

    /// <summary>
    /// Checks if the size of a table exceeds a specified threshold.
    /// </summary>
    /// <param name="schema">The table schema.</param>
    /// <param name="table">The table name.</param>
    /// <param name="sizeThreshold">The size threshold.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> if the table size is smaller than <paramref name="sizeThreshold"/>, otherwise <see langword="false"/>.</returns>
    public ValueTask<bool> IsTableSmallerThan(
        string schema,
        string table,
        ByteSize sizeThreshold,
        CancellationToken cancellationToken = default)
        => WithCache(
            (schema, table, sizeThreshold),
            static (self, cache, state) =>
            {
                var size = cache.SizeFor(state.schema, state.table);
                if (size >= state.sizeThreshold)
                {
                    Log.TableSizeExceedsThreshold(self._logger, state.schema, state.table, size, state.sizeThreshold);
                    return false;
                }

                return true;
            },
            cancellationToken);

    private ValueTask<TResult> WithCache<TState, TResult>(
        TState state,
        Func<PostgreSqlSizeService, Cache, TState, TResult> func,
        CancellationToken cancellationToken)
    {
        var cacheTask = GetCache(cancellationToken);
        if (!cacheTask.IsCompletedSuccessfully)
        {
            return WaitFor(this, cacheTask, state, func);
        }

        return ValueTask.FromResult(func(this, cacheTask.Result, state));

        static async ValueTask<TResult> WaitFor(
            PostgreSqlSizeService self,
            ValueTask<Cache> cacheTask,
            TState state,
            Func<PostgreSqlSizeService, Cache, TState, TResult> func)
            => func(self, await cacheTask, state);
    }

    private ValueTask<Cache> GetCache(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var cache = Volatile.Read(ref _cache);
        if (cache is not null && cache.IsFresh(now))
        {
            return ValueTask.FromResult(cache);
        }

        if (cache is not null && cache.IsStale(now))
        {
            _ = Task.Run(_refreshCacheAction, _lifetime.ApplicationStopping);
            return ValueTask.FromResult(cache);
        }

        return new(MaybeRefreshCacheAsync(cache, cancellationToken));
    }

    private Task<Cache> MaybeRefreshCacheAsync(Cache? oldCache, CancellationToken cancellationToken)
    {
        TaskCompletionSource<Cache> tcs = new();

        lock (_lock)
        {
            var cache = Volatile.Read(ref _cache);
            if (cache is not null && !ReferenceEquals(cache, oldCache))
            {
                // If the cache has been updated while we were waiting for the lock, return the new cache.
                return Task.FromResult(cache);
            }

            var cacheTask = _cacheTask;
            if (cacheTask is not null)
            {
                return cacheTask.WaitAsync(cancellationToken);
            }

            _cacheTask = tcs.Task;
        }

        _ = RefreshCacheAsync(tcs);
        return tcs.Task.WaitAsync(cancellationToken);
    }

    private Task RefreshCacheAsync()
    {
        TaskCompletionSource<Cache> tcs = new();

        lock (_lock)
        {
            if (_cacheTask is not null)
            {
                // If a refresh is already in progress, return the existing task.
                return _cacheTask;
            }

            _cacheTask = tcs.Task;
        }

        _ = RefreshCacheAsync(tcs);
        return tcs.Task;
    }

    private async Task RefreshCacheAsync(TaskCompletionSource<Cache> tcs)
    {
        const string QUERY =
            /*strpsql*/"""
            SELECT
                t.relname "table",
                t.schemaname "schema",
                PG_TOTAL_RELATION_SIZE(t.relid) "size"
            FROM pg_catalog.pg_statio_user_tables t
            """;

        var cancellationToken = _lifetime.ApplicationStopping;

        try
        {
            using var activity = RegisterTelemetry.StartActivity("refresh postgresql size-cache", ActivityKind.Internal);
            using var connection = await _db.OpenConnectionAsync(cancellationToken);
            using var command = connection.CreateCommand();

            command.CommandText = QUERY;

            await command.PrepareAsync(cancellationToken);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var tableIndex = reader.GetOrdinal("table");
            var schemaIndex = reader.GetOrdinal("schema");
            var sizeIndex = reader.GetOrdinal("size");

            var sizes = new Dictionary<(string Schema, string Table), ByteSize>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var table = await reader.GetFieldValueAsync<string>(tableIndex, cancellationToken);
                var schema = await reader.GetFieldValueAsync<string>(schemaIndex, cancellationToken);
                var size = ByteSize.FromBytes(checked((ulong)await reader.GetFieldValueAsync<long>(sizeIndex, cancellationToken)));

                sizes[(schema, table)] = size;
            }

            var now = _timeProvider.GetUtcNow();
            var cache = new Cache(now, sizes);

            lock (_lock)
            {
                _cache = cache;
                _cacheTask = null;
            }

            tcs.TrySetResult(cache);
        }
        catch (OperationCanceledException ex)
        {
            lock (_lock)
            {
                _cacheTask = null;
            }

            tcs.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _cacheTask = null;
            }

            tcs.TrySetException(ex);
        }
    }

    private sealed class Cache(DateTimeOffset timestamp, Dictionary<(string Schema, string Table), ByteSize> sizes)
    {
        private readonly DateTimeOffset _timestamp = timestamp;
        private readonly Dictionary<(string Schema, string Table), ByteSize> _sizes = sizes;
        private readonly KeyValuePair<(string Schema, string Table), ByteSize> _biggestTable = sizes.OrderByDescending(kvp => kvp.Key).FirstOrDefault();
        private readonly ByteSize _totalSize = ByteSize.FromBytes(checked((ulong)sizes.Values.Select(v => checked((long)v.Bytes)).Sum()));

        public ByteSize TotalSize => _totalSize;

        public (string Schema, string Table, ByteSize Size) BiggestTable
            => (_biggestTable.Key.Schema, _biggestTable.Key.Table, _biggestTable.Value);

        public ByteSize SizeFor(string schema, string table)
        {
            return _sizes.TryGetValue((schema, table), out var size) ? size : ByteSize.Zero;
        }

        public bool IsFresh(DateTimeOffset now)
        {
            return now - _timestamp < TimeSpan.FromMinutes(5);
        }

        public bool IsStale(DateTimeOffset now)
        {
            return now - _timestamp >= TimeSpan.FromMinutes(15);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Table '{Schema}.{Table}' size {Size} exceeds threshold {Threshold}.")]
        public static partial void TableSizeExceedsThreshold(ILogger logger, string schema, string table, ByteSize size, ByteSize threshold);

        [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Database size {Size} exceeds threshold {Threshold}.")]
        public static partial void DatabaseSizeExceedsTheshold(ILogger logger, ByteSize size, ByteSize threshold);
    }
}
