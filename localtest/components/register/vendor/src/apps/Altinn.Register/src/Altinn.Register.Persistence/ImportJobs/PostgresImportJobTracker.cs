using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Altinn.Register.Core;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Polly;
using Polly.Retry;
using Polly.Telemetry;

namespace Altinn.Register.Persistence.ImportJobs;

/// <summary>
/// Implementation of <see cref="IImportJobTracker"/> that uses a hybrid approach of in-memory caching and database
/// calls, using a background worker to process messages from a channel.
/// </summary>
/// <remarks>
/// This class must be registered as a singleton, due to among other things the <see cref="_retryPipeline"/> being expensive to create.
/// </remarks>
internal partial class PostgresImportJobTracker
    : IImportJobTracker
    , IAsyncDisposable
{
    private static readonly ResiliencePropertyKey<Activity?> ActivityPropertyKey = new($"{nameof(PostgresImportJobTracker)}.{nameof(Activity)}");

    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<PostgresImportJobTracker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ChannelWriter<WorkerMessage> _writer;
    private readonly ResiliencePipeline _retryPipeline;
    private readonly Task _runTask;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresImportJobTracker"/> class.
    /// </summary>
    public PostgresImportJobTracker(
        ILoggerFactory loggerFactory,
        ILogger<PostgresImportJobTracker> logger,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;

        var telemetryOptions = new TelemetryOptions
        {
            LoggerFactory = loggerFactory,
        };

        var pipelineBuilder = new ResiliencePipelineBuilder();
        pipelineBuilder.TimeProvider = timeProvider;
        pipelineBuilder.AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder()
                .Handle<JobDoesNotExistException>()
                .Handle<PostgresException>(static e =>
                {
                    if (e.SqlState == PostgresErrorCodes.SerializationFailure)
                    {
                        // retry all serialization failures
                        return true;
                    }

                    if (e.SqlState == PostgresErrorCodes.CheckViolation)
                    {
                        // if the constraint violated is due to the updated value being greater than the source value, retry
                        // assuming the source value has been updated since the last read
                        return e.ConstraintName is "enqueued_max_less_than_or_equal_to_source_max" or "processed_max_less_than_or_equal_to_enqueued_max";
                    }

                    return false;
                }),
            BackoffType = DelayBackoffType.Constant,
            UseJitter = false,
            MaxRetryAttempts = 5,
            DelayGenerator = static args => new(TimeSpan.FromMilliseconds(10 * args.AttemptNumber)),
            OnRetry = args =>
            {
                var activity = args.Context.Properties.GetValue(ActivityPropertyKey, defaultValue: null);
                activity?.SetTag("retry.count", args.AttemptNumber + 1);
                Log.TransactionSerializationError(logger);

                return ValueTask.CompletedTask;
            },
        });

        _retryPipeline = pipelineBuilder
            .ConfigureTelemetry(telemetryOptions)
            .Build();

        var channel = Channel.CreateUnbounded<WorkerMessage>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = true,
            SingleReader = true,
            SingleWriter = false,
        });

        _writer = channel.Writer;
        _runTask = Run(channel.Reader, _cts.Token);
    }

    /// <summary>
    /// Clears the cache for the given job id.
    /// </summary>
    /// <param name="id">The job id.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>Whether or not there was any cache entry to clear.</returns>
    internal async Task<bool> ClearCache(string id, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>();
        var message = new WorkerMessage.ClearCacheMessage(id, Activity.Current, cancellationToken, tcs);
        await _writer.WriteAsync(message, cancellationToken);
        return await tcs.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    async Task<ImportJobStatus> IImportJobTracker.GetStatus(string id, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<ImportJobStatus>();
        var message = new WorkerMessage.GetStatusMessage(id, Activity.Current, cancellationToken, tcs);
        await _writer.WriteAsync(message, cancellationToken);
        return await tcs.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    async Task<(ImportJobStatus Status, bool Updated)> IImportJobTracker.TrackQueueStatus(string id, ImportJobQueueStatus status, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<ImportJobUpdateStatus>();
        var message = new WorkerMessage.TrackQueueStatusMessage(id, status, Activity.Current, cancellationToken, tcs);
        await _writer.WriteAsync(message, cancellationToken);
        var (newStatus, updated) = await tcs.Task.WaitAsync(cancellationToken);
        return (newStatus, updated);
    }

    /// <inheritdoc/>
    async Task<(ImportJobStatus Status, bool Updated)> IImportJobTracker.TrackProcessedStatus(string id, ImportJobProcessingStatus status, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<ImportJobUpdateStatus>();
        var message = new WorkerMessage.TrackProcessedStatusMessage(id, status, Activity.Current, cancellationToken, tcs);
        await _writer.WriteAsync(message, cancellationToken);
        var (newStatus, updated) = await tcs.Task.WaitAsync(cancellationToken);
        return (newStatus, updated);
    }

    /// <inheritdoc/>
    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
        {
            await _cts.CancelAsync();
            await _runTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        _cts.Dispose();
    }

    private async Task Run(ChannelReader<WorkerMessage> reader, CancellationToken cancellationToken)
    {
        Dictionary<string, ImportJobStatus> cache = new();

        while (!cancellationToken.IsCancellationRequested)
        {
            var done = !await reader.WaitToReadAsync(cancellationToken);
            if (done)
            {
                break;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            try
            {
                await RunInner(reader, scope.ServiceProvider, cache, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                Log.ProcessingError(_logger, e);
            }
        }
    }

    private async Task RunInner(ChannelReader<WorkerMessage> reader, IServiceProvider services, Dictionary<string, ImportJobStatus> cache, CancellationToken cancellationToken)
    {
        var dataSource = services.GetRequiredService<NpgsqlDataSource>();
        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // allow keeping the connection for a short while
                var done = !await reader.WaitToReadAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(1), _timeProvider);

                if (done)
                {
                    // no more items, channel is closed
                    break;
                }
            }
            catch (TimeoutException)
            {
                // release the db connection, return to outer loop
                break;
            }

            if (!reader.TryRead(out var message))
            {
                // this in general shouldn't happen as we've already waited for a message
                continue;
            }

            Activity.Current = message.Activity;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(message.CancellationToken, cancellationToken);

            switch (message)
            {
                case WorkerMessage.ClearCacheMessage { Id: var id, CompletionSource: var tcs }:
                    {
                        if (cts.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled(cts.Token);
                            break;
                        }

                        var result = cache.Remove(id);
                        tcs.TrySetResult(result);
                        break;
                    }

                case WorkerMessage.GetStatusMessage { Id: var id, CompletionSource: var tcs }:
                    {
                        var task = GetStatus(conn, cache, id, cts.Token);
                        await ((Task)task).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                        tcs.TrySetFromTask(task);
                        break;
                    }

                case WorkerMessage.TrackQueueStatusMessage { Id: var id, Status: var status, CompletionSource: var tcs }:
                    {
                        var task = TrackQueueStatus(conn, cache, id, status, cts.Token);
                        await ((Task)task).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                        tcs.TrySetFromTask(task);
                        break;
                    }

                case WorkerMessage.TrackProcessedStatusMessage { Id: var id, Status: var status, CompletionSource: var tcs }:
                    {
                        var task = TrackProcessedStatus(conn, cache, id, status, cts.Token);
                        await ((Task)task).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                        tcs.TrySetFromTask(task);
                        break;
                    }
            }
        }
    }

    private async Task<ImportJobStatus> GetStatus(NpgsqlConnection conn, Dictionary<string, ImportJobStatus> cache, string id, CancellationToken cancellationToken = default)
    {
        const string QUERY =
            /*strpsql*/"""
            SELECT source_max, enqueued_max, processed_max
            FROM register.import_job
            WHERE id = @id
            """;

        var status = await WithConnection(
            conn,
            id,
            static async (id, conn, logger, cancellationToken) =>
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = QUERY;

                cmd.Parameters.Add<string>("id", NpgsqlDbType.Text).TypedValue = id;

                await cmd.PrepareAsync(cancellationToken);
                await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    // Job is not yet created
                    return default;
                }

                var sourceMax = await reader.GetFieldValueOrDefaultAsync<long?>("source_max", cancellationToken) switch { null => (ulong?)null, long l => (ulong)l };
                var enqueuedMax = await reader.GetFieldValueAsync<long>("enqueued_max", cancellationToken);
                var processedMax = await reader.GetFieldValueAsync<long>("processed_max", cancellationToken);

                return new ImportJobStatus
                {
                    SourceMax = sourceMax,
                    EnqueuedMax = (ulong)enqueuedMax,
                    ProcessedMax = (ulong)processedMax,
                };
            },
            _logger,
            cancellationToken);

        cache[id] = status;
        return status;
    }

    private async Task<ImportJobUpdateStatus> TrackQueueStatus(
        NpgsqlConnection conn,
        Dictionary<string, ImportJobStatus> cache,
        string id,
        ImportJobQueueStatus status,
        CancellationToken cancellationToken = default)
    {
        const string QUERY =
            /*strpsql*/"""
            WITH existing AS (
                SELECT source_max, enqueued_max, processed_max, 0 as source
                FROM register.import_job
                WHERE id = @id
            ), updated AS (
                INSERT INTO register.import_job (id, source_max, enqueued_max, processed_max)
                VALUES (@id, @source_max, @enqueued_max, 0)
                ON CONFLICT (id) DO UPDATE
                    SET source_max = GREATEST(import_job.source_max, EXCLUDED.source_max),
                        enqueued_max = GREATEST(import_job.enqueued_max, EXCLUDED.enqueued_max)
                    WHERE import_job.enqueued_max < EXCLUDED.enqueued_max
                        OR import_job.source_max < EXCLUDED.source_max
                RETURNING source_max, enqueued_max, processed_max, 1 as source
            )
            SELECT source_max, enqueued_max, processed_max, source FROM updated
            UNION SELECT source_max, enqueued_max, processed_max, source FROM existing
            ORDER BY source DESC
            LIMIT 1
            """;

        if (cache.TryGetValue(id, out var cached) && cached.EnqueuedMax >= status.EnqueuedMax && cached.SourceMax >= status.SourceMax)
        {
            return new(cached, false);
        }

        var (sourceMax, enqueuedMax, processedMax, updated) = await WithConnection(
            conn,
            id,
            static async (id, conn, state, cancellationToken) =>
            {
                var (status, logger) = state;
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = QUERY;

                cmd.Parameters.Add<string>("id", NpgsqlDbType.Text).TypedValue = id;
                cmd.Parameters.Add<long?>("source_max", NpgsqlDbType.Bigint).TypedValue = status.SourceMax switch { null => null, ulong u => checked((long)u) };
                cmd.Parameters.Add<long>("enqueued_max", NpgsqlDbType.Bigint).TypedValue = checked((long)status.EnqueuedMax);

                Log.UpdateQueueStatus(logger, id, status);
                await cmd.PrepareAsync(cancellationToken);
                await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow | CommandBehavior.SingleResult, cancellationToken);

                var read = await reader.ReadAsync(cancellationToken);
                Debug.Assert(read);

                var sourceMax = await reader.GetFieldValueOrDefaultAsync<long?>("source_max", cancellationToken) switch { null => (ulong?)null, long l => (ulong)l };
                var enqueuedMax = (ulong)await reader.GetFieldValueAsync<long>("enqueued_max", cancellationToken);
                var processedMax = (ulong)await reader.GetFieldValueAsync<long>("processed_max", cancellationToken);
                var source = await reader.GetFieldValueAsync<int>("source", cancellationToken);
                var updated = source == 1;

                return (sourceMax, enqueuedMax, processedMax, updated);
            },
            new WithLoggerState<ImportJobQueueStatus>(status, _logger),
            cancellationToken);

        cached = new() { EnqueuedMax = enqueuedMax, SourceMax = sourceMax, ProcessedMax = processedMax };
        cache[id] = cached;
        return new(cached, updated);
    }

    private async Task<ImportJobUpdateStatus> TrackProcessedStatus(
        NpgsqlConnection conn,
        Dictionary<string, ImportJobStatus> cache,
        string id,
        ImportJobProcessingStatus status,
        CancellationToken cancellationToken = default)
    {
        const string QUERY =
            /*strpsql*/"""
            WITH existing AS (
                SELECT source_max, enqueued_max, processed_max, 0 as source
                FROM register.import_job
                WHERE id = @id
            ), updated AS (
                UPDATE register.import_job
                SET processed_max = @processed_max
                WHERE id = @id AND processed_max < @processed_max
                RETURNING source_max, enqueued_max, processed_max, 1 as source
            )
            SELECT source_max, enqueued_max, processed_max, source FROM updated
            UNION SELECT source_max, enqueued_max, processed_max, source FROM existing
            ORDER BY source DESC
            LIMIT 1
            """;

        if (cache.TryGetValue(id, out var cached) && cached.ProcessedMax >= status.ProcessedMax)
        {
            return new(cached, false);
        }

        var (sourceMax, enqueuedMax, processedMax, updated) = await WithConnection(
            conn,
            id,
            static async (id, conn, state, cancellationToken) =>
            {
                using var activity = RegisterTelemetry.StartActivity(
                    $"{nameof(TrackProcessedStatus)}.attempt",
                    tags: [
                        new("job.name", id),
                    ]);

                try
                {
                    var (status, logger) = state;
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = QUERY;

                    cmd.Parameters.Add<string>("id", NpgsqlDbType.Text).TypedValue = id;
                    cmd.Parameters.Add<long>("processed_max", NpgsqlDbType.Bigint).TypedValue = checked((long)status.ProcessedMax);

                    Log.UpdateProcessedStatus(logger, id, status);
                    await cmd.PrepareAsync(cancellationToken);
                    await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow, cancellationToken);

                    var read = await reader.ReadAsync(cancellationToken);
                    if (!read)
                    {
                        throw new JobDoesNotExistException(id);
                    }

                    var sourceMax = await reader.GetFieldValueOrDefaultAsync<long?>("source_max", cancellationToken) switch { null => (ulong?)null, long l => (ulong)l };
                    var enqueuedMax = (ulong)await reader.GetFieldValueAsync<long>("enqueued_max", cancellationToken);
                    var processedMax = (ulong)await reader.GetFieldValueAsync<long>("processed_max", cancellationToken);
                    var source = await reader.GetFieldValueAsync<int>("source", cancellationToken);
                    var updated = source == 1;

                    return (sourceMax, enqueuedMax, processedMax, updated);
                }
                catch (Exception ex) when (activity is not null)
                {
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                    throw;
                }
            },
            new WithLoggerState<ImportJobProcessingStatus>(status, _logger),
            cancellationToken);

        cached = new() { EnqueuedMax = enqueuedMax, SourceMax = sourceMax, ProcessedMax = processedMax };
        cache[id] = cached;
        return new(cached, updated);
    }

    private async Task<TResult> WithConnection<TResult, TState>(
        NpgsqlConnection conn,
        string id,
        Func<string, NpgsqlConnection, TState, CancellationToken, Task<TResult>> action,
        TState state,
        CancellationToken cancellationToken,
        [CallerMemberName] string? activityName = null)
    {
        using var activity = RegisterTelemetry.StartActivity(
            $"{nameof(PostgresImportJobTracker)}.{activityName}",
            ActivityKind.Internal,
            tags: [
                new("import-job.id", id),
                new("retry.count", 0),
            ]);

        ResilienceContext ctx = ResilienceContextPool.Shared.Get(cancellationToken);
        var success = false;
        try
        {
            ctx.Properties.Set(ActivityPropertyKey, activity);

            var txCtx = new WithConnectionContext<TResult, TState>(id, state, action, conn);

            var result = await _retryPipeline.ExecuteAsync(
                static async (ResilienceContext ctx, WithConnectionContext<TResult, TState> connCtx) =>
                {
                    var cancellationToken = ctx.CancellationToken;
                    var (id, state, action, conn) = connCtx;

                    return await action(id, conn, state, cancellationToken);
                },
                ctx,
                txCtx);

            success = true;
            return result!;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.CheckViolation && e.ConstraintName
            is "source_max_positive"
            or "enqueued_max_positive"
            or "processed_max_positive"
            or "enqueued_max_less_than_or_equal_to_source_max"
            or "processed_max_less_than_or_equal_to_enqueued_max")
        {
            var msg = e.ConstraintName switch
            {
                "source_max_positive" => "Source max must be positive",
                "enqueued_max_positive" => "Enqueued max must be positive",
                "processed_max_positive" => "Processed max must be positive",
                "enqueued_max_less_than_or_equal_to_source_max" => "Enqueued max must be less than or equal to source max",
                "processed_max_less_than_or_equal_to_enqueued_max" => "Processed max must be less than or equal to enqueued max",
                _ => Unreachable<string>(e),
            };

            throw new InvalidOperationException(msg, e);
        }
        finally
        {
            if (!success)
            {
                activity?.SetTag("error", "true");
            }

            ResilienceContextPool.Shared.Return(ctx);
        }

        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        static T Unreachable<T>(Exception inner) => throw new UnreachableException(null, inner);
    }

    private readonly record struct WithConnectionContext<TResult, TState>(
        string Id,
        TState State,
        Func<string, NpgsqlConnection, TState, CancellationToken, Task<TResult>> Action,
        NpgsqlConnection Connection);

    private readonly record struct WithLoggerState<T>(T State, ILogger Logger);

    private sealed class JobDoesNotExistException(string jobName)
        : InvalidOperationException($"Job '{jobName}' does not exist")
    {
    }

    private readonly record struct ImportJobUpdateStatus(ImportJobStatus Status, bool Updated);

    private abstract record WorkerMessage(string Id, Activity? Activity, CancellationToken CancellationToken)
    {
        public sealed record ClearCacheMessage(string Id, Activity? Activity, CancellationToken CancellationToken, TaskCompletionSource<bool> CompletionSource)
            : WorkerMessage(Id, Activity, CancellationToken);

        public sealed record GetStatusMessage(string Id, Activity? Activity, CancellationToken CancellationToken, TaskCompletionSource<ImportJobStatus> CompletionSource)
            : WorkerMessage(Id, Activity, CancellationToken);

        public sealed record TrackQueueStatusMessage(string Id, ImportJobQueueStatus Status, Activity? Activity, CancellationToken CancellationToken, TaskCompletionSource<ImportJobUpdateStatus> CompletionSource)
            : WorkerMessage(Id, Activity, CancellationToken);

        public sealed record TrackProcessedStatusMessage(string Id, ImportJobProcessingStatus Status, Activity? Activity, CancellationToken CancellationToken, TaskCompletionSource<ImportJobUpdateStatus> CompletionSource)
            : WorkerMessage(Id, Activity, CancellationToken);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Debug, "Failed to commit transaction due to serialization error")]
        public static partial void TransactionSerializationError(ILogger logger);

        [LoggerMessage(1, LogLevel.Debug, "Updating queue status for job {JobId} with source max {SourceMax} and enqueued max {EnqueuedMax}")]
        private static partial void UpdateQueueStatus(ILogger logger, string jobId, ulong? sourceMax, ulong enqueuedMax);

        public static void UpdateQueueStatus(ILogger logger, string jobId, ImportJobQueueStatus status)
            => UpdateQueueStatus(logger, jobId, status.SourceMax, status.EnqueuedMax);

        [LoggerMessage(2, LogLevel.Debug, "Updating processed status for job {JobId} with processed max {ProcessedMax}")]
        private static partial void UpdateProcessedStatus(ILogger logger, string jobId, ulong processedMax);

        public static void UpdateProcessedStatus(ILogger logger, string jobId, ImportJobProcessingStatus status)
            => UpdateProcessedStatus(logger, jobId, status.ProcessedMax);

        [LoggerMessage(3, LogLevel.Error, "Error processing messages")]
        public static partial void ProcessingError(ILogger logger, Exception exception);
    }
}
