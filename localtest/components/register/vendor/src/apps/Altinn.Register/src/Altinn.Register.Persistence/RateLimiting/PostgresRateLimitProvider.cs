using System.Data;
using System.Diagnostics;
using Altinn.Authorization.ServiceDefaults.Npgsql;
using Altinn.Register.Core.RateLimiting;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Polly;
using Polly.Retry;

namespace Altinn.Register.Persistence.RateLimiting;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IRateLimitProvider"/>.
/// </summary>
internal sealed partial class PostgresRateLimitProvider
    : IRateLimitProvider
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PostgresRateLimitProvider> _logger;
    private readonly ResiliencePipeline<RateLimitStatus> _pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresRateLimitProvider"/> class.
    /// </summary>
    public PostgresRateLimitProvider(
        NpgsqlDataSource dataSource,
        TimeProvider timeProvider,
        ILogger<PostgresRateLimitProvider> logger)
    {
        _dataSource = dataSource;
        _timeProvider = timeProvider;
        _logger = logger;

        _pipeline = CreateRetryPipeline(timeProvider, logger);
    }

    /// <inheritdoc/>
    public async ValueTask<RateLimitStatus> GetStatus(
        string policyName,
        string resource,
        string subject,
        BlockedRequestBehavior blockedRequestBehavior,
        TimeSpan blockDuration,
        CancellationToken cancellationToken = default)
    {
        const string QUERY =
            /*strpsql*/"""
            SELECT o.outcome, o.count, o.window_started_at, o.window_expires_at, o.blocked_until, o.is_blocked
            FROM register.rate_limit_get_status(@policy, @resource, @subject, @now, @blocked_request_behavior, @block_duration) AS o
            """;

        Guard.IsNotNullOrEmpty(policyName);
        Guard.IsNotNull(resource);
        Guard.IsNotNullOrEmpty(subject);
        Guard.IsGreaterThan(blockDuration, TimeSpan.Zero);
        var now = _timeProvider.GetUtcNow();

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await _pipeline.ExecuteAsync(
            callback: static async (s, cancellationToken) =>
            {
                var (conn, policyName, resource, subject, blockedRequestBehavior, blockDuration, now) = s;
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await using var cmd = conn.CreateCommand(QUERY);

                cmd.Parameters.Add<string>("policy", NpgsqlDbType.Text).TypedValue = policyName;
                cmd.Parameters.Add<string>("resource", NpgsqlDbType.Text).TypedValue = resource;
                cmd.Parameters.Add<string>("subject", NpgsqlDbType.Text).TypedValue = subject;
                cmd.Parameters.Add<DateTimeOffset>("now", NpgsqlDbType.TimestampTz).TypedValue = now;
                cmd.Parameters.Add<BlockedRequestBehavior>("blocked_request_behavior").TypedValue = blockedRequestBehavior;
                cmd.Parameters.Add<TimeSpan>("block_duration", NpgsqlDbType.Interval).TypedValue = blockDuration;

                await cmd.PrepareAsync(cancellationToken);

                DbStatus result;
                {
                    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    var read = await reader.ReadAsync(cancellationToken);
                    Debug.Assert(read);

                    result = await DbStatus.ReadAsync(reader, cancellationToken);
                }

                await tx.CommitAsync(cancellationToken);
                return ToRateLimitStatus(result, Operation.GetStatus);
            },
            state: (conn, policyName, resource, subject, blockedRequestBehavior, blockDuration, now),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<RateLimitStatus> Record(
        string policyName,
        string resource,
        string subject,
        ushort cost,
        int limit,
        TimeSpan windowDuration,
        RateLimitWindowBehavior windowBehavior,
        TimeSpan blockDuration,
        CancellationToken cancellationToken = default)
    {
        const string LEADING_EDGE_QUERY =
            /*strpsql*/"""
            SELECT o.outcome, o.count, o.window_started_at, o.window_expires_at, o.blocked_until, o.is_blocked
            FROM register.rate_limit_record_leading_edge(@policy, @resource, @subject, @now, @cost, @limit, @window_duration, @block_duration) AS o
            """;

        const string TRAILING_EDGE_QUERY =
            /*strpsql*/"""
            SELECT o.outcome, o.count, o.window_started_at, o.window_expires_at, o.blocked_until, o.is_blocked
            FROM register.rate_limit_record_trailing_edge(@policy, @resource, @subject, @now, @cost, @limit, @window_duration, @block_duration) AS o
            """;

        Guard.IsNotNullOrEmpty(policyName);
        Guard.IsNotNull(resource);
        Guard.IsNotNullOrEmpty(subject);
        Guard.IsGreaterThan(cost, (ushort)0);
        Guard.IsGreaterThan(limit, 0);
        Guard.IsGreaterThan(windowDuration, TimeSpan.Zero);
        Guard.IsGreaterThan(blockDuration, TimeSpan.Zero);
        var now = _timeProvider.GetUtcNow();
        var query = windowBehavior switch
        {
            RateLimitWindowBehavior.LeadingEdge => LEADING_EDGE_QUERY,
            RateLimitWindowBehavior.TrailingEdge => TRAILING_EDGE_QUERY,
            _ => throw new UnreachableException($"Unreachable window behavior '{windowBehavior}'."),
        };

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await _pipeline.ExecuteAsync(
            callback: static async (s, cancellationToken) =>
            {
                var (conn, query, policyName, resource, subject, now, cost, limit, windowDuration, blockDuration) = s;
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await using var cmd = conn.CreateCommand(query);

                cmd.Parameters.Add<string>("policy", NpgsqlDbType.Text).TypedValue = policyName;
                cmd.Parameters.Add<string>("resource", NpgsqlDbType.Text).TypedValue = resource;
                cmd.Parameters.Add<string>("subject", NpgsqlDbType.Text).TypedValue = subject;
                cmd.Parameters.Add<DateTimeOffset>("now", NpgsqlDbType.TimestampTz).TypedValue = now;
                cmd.Parameters.Add<int>("cost", NpgsqlDbType.Integer).TypedValue = cost;
                cmd.Parameters.Add<int>("limit", NpgsqlDbType.Integer).TypedValue = limit;
                cmd.Parameters.Add<TimeSpan>("window_duration", NpgsqlDbType.Interval).TypedValue = windowDuration;
                cmd.Parameters.Add<TimeSpan>("block_duration", NpgsqlDbType.Interval).TypedValue = blockDuration;

                await cmd.PrepareAsync(cancellationToken);

                DbStatus result;
                {
                    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    var read = await reader.ReadAsync(cancellationToken);
                    Debug.Assert(read);

                    result = await DbStatus.ReadAsync(reader, cancellationToken);
                }

                await tx.CommitAsync(cancellationToken);
                return ToRateLimitStatus(result, Operation.Record);
            },
            state: (conn, query, policyName, resource, subject, now, cost, limit, windowDuration, blockDuration),
            cancellationToken: cancellationToken);
    }

    private static ResiliencePipeline<RateLimitStatus> CreateRetryPipeline(TimeProvider timeProvider, ILogger logger)
    {
        var pipelineBuilder = new ResiliencePipelineBuilder<RateLimitStatus>();
        pipelineBuilder.TimeProvider = timeProvider;
        pipelineBuilder.AddRetry(new RetryStrategyOptions<RateLimitStatus>
        {
            ShouldHandle = new PredicateBuilder<RateLimitStatus>()
                .Handle<PostgresException>(e => e.SqlState == PostgresErrorCodes.SerializationFailure),
            BackoffType = DelayBackoffType.Constant,
            UseJitter = false,
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(10),
            OnRetry = args =>
            {
                Log.RateLimitOperationSerializationFailure(logger, args.AttemptNumber);
                return ValueTask.CompletedTask;
            },
        });

        return pipelineBuilder.Build();
    }

    private static RateLimitStatus ToRateLimitStatus(DbStatus result, Operation operation)
    {
        switch (operation)
        {
            case Operation.GetStatus when result.Outcome == 0:
                return RateLimitStatus.NotFound;

            case Operation.GetStatus when result.Outcome == 1:
            case Operation.Record when result.Outcome == 0:
                break; // Success cases, handled below.

            default:
                return UnreachableOutcome<RateLimitStatus>(operation.ToString(), result.Outcome);
        }

        Debug.Assert(result.Count.HasValue);
        Debug.Assert(result.WindowStartedAt.HasValue);
        Debug.Assert(result.WindowExpiresAt.HasValue);
        Debug.Assert(!result.IsBlocked || result.BlockedUntil.HasValue);

        return RateLimitStatus.Found(
            count: checked((uint)result.Count.Value),
            windowStartedAt: result.WindowStartedAt.Value,
            windowExpiresAt: result.WindowExpiresAt.Value,
            blockedUntil: result.IsBlocked ? result.BlockedUntil : null);
    }

    private static T UnreachableOutcome<T>(string resultName, short outcome)
        => throw new UnreachableException($"Unreachable outcome '{outcome}' encountered when reading {resultName}.");

    private sealed record DbStatus(
        short Outcome,
        int? Count,
        DateTimeOffset? WindowStartedAt,
        DateTimeOffset? WindowExpiresAt,
        DateTimeOffset? BlockedUntil,
        bool IsBlocked)
    {
        public static async ValueTask<DbStatus> ReadAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
        {
            var outcome = await reader.GetFieldValueAsync<short>("outcome", cancellationToken);
            var count = await reader.GetFieldValueAsync<int?>("count", cancellationToken);
            var windowStartedAt = await reader.GetFieldValueAsync<DateTimeOffset?>("window_started_at", cancellationToken);
            var windowExpiresAt = await reader.GetFieldValueAsync<DateTimeOffset?>("window_expires_at", cancellationToken);
            var blockedUntil = await reader.GetFieldValueAsync<DateTimeOffset?>("blocked_until", cancellationToken);
            var isBlocked = await reader.GetFieldValueAsync<bool>("is_blocked", cancellationToken);

            return new(outcome, count, windowStartedAt, windowExpiresAt, blockedUntil, isBlocked);
        }
    }

    private enum Operation
    {
        GetStatus = 1,
        Record,
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "Rate-limit operation failed due to a serialization error. Retrying attempt {AttemptNumber}.")]
        public static partial void RateLimitOperationSerializationFailure(ILogger logger, int attemptNumber);
    }
}
