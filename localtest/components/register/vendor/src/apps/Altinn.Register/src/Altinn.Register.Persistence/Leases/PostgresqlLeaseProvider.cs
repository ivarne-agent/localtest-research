using System.Data;
using System.Diagnostics;
using Altinn.Authorization.ServiceDefaults.Leases;
using Altinn.Authorization.ServiceDefaults.Npgsql;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Polly;
using Polly.Retry;

namespace Altinn.Register.Persistence.Leases;

/// <summary>
/// Implementation of <see cref="ILeaseProvider"/> that uses a postgresql database
/// as lease storage.
/// </summary>
internal partial class PostgresqlLeaseProvider
    : ILeaseProvider
{
    private static readonly TimeSpan MAX_LEASE_DURATION = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MIN_LEASE_DURATION = TimeSpan.FromSeconds(30);

    private readonly NpgsqlDataSource _dataSource;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PostgresqlLeaseProvider> _logger;

    private readonly ResiliencePipeline<AcquireResult> _acquirePipeline;
    private readonly ResiliencePipeline<RenewResult> _rewnewPipeline;
    private readonly ResiliencePipeline<ReleaseResult> _releasePipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresqlLeaseProvider"/> class.
    /// </summary>
    public PostgresqlLeaseProvider(
        NpgsqlDataSource dataSource,
        TimeProvider timeProvider,
        ILogger<PostgresqlLeaseProvider> logger)
    {
        _dataSource = dataSource;
        _timeProvider = timeProvider;
        _logger = logger;

        _acquirePipeline = CreateRetryPipeline<AcquireResult>("acquire", timeProvider, logger);
        _rewnewPipeline = CreateRetryPipeline<RenewResult>("renew", timeProvider, logger);
        _releasePipeline = CreateRetryPipeline<ReleaseResult>("release", timeProvider, logger);
    }

    /// <inheritdoc/>
    public async Task<LeaseAcquireResult> TryAcquireLease(
        string leaseId,
        TimeSpan duration,
        TimeSpan? ifUnacquiredFor = null,
        CancellationToken cancellationToken = default)
    {
        const string QUERY =
            /*strpsql*/"""
            SELECT o.outcome, o.id, o.token, o.expires, o.acquired, o.released, o.condition
            FROM register.lease_acquire(@id, @now, @expires, @condition_released_before) AS o
            """;

        Guard.IsNotNullOrEmpty(leaseId);
        Guard.IsLessThan(duration, MAX_LEASE_DURATION);
        Guard.IsGreaterThan(duration, MIN_LEASE_DURATION);

        var now = _timeProvider.GetUtcNow();
        var expires = now + duration;
        var conditionReleasedBefore = ifUnacquiredFor.HasValue
            ? now - ifUnacquiredFor.Value
            : (DateTimeOffset?)null;

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await _acquirePipeline.ExecuteAsync(
            callback: static async (s, cancellationToken) =>
            {
                var (self, conn, leaseId, now, expires, conditionReleasedBefore) = s;
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await using var cmd = conn.CreateCommand(QUERY);

                cmd.Parameters.Add<string>("id", NpgsqlDbType.Text).TypedValue = leaseId;
                cmd.Parameters.Add<DateTimeOffset>("now", NpgsqlDbType.TimestampTz).TypedValue = now;
                cmd.Parameters.Add<DateTimeOffset>("expires", NpgsqlDbType.TimestampTz).TypedValue = expires;
                cmd.Parameters.Add<DateTimeOffset?>("condition_released_before", NpgsqlDbType.TimestampTz).TypedValue = conditionReleasedBefore;

                await cmd.PrepareAsync(cancellationToken);
                AcquireResult result;

                {
                    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    var read = await reader.ReadAsync(cancellationToken);
                    Debug.Assert(read);

                    result = await AcquireResult.ReadAsync(reader, cancellationToken);
                }

                if (result is AcquireResult.Success)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                return result;
            },
            state: (this, conn, leaseId, now, expires, conditionReleasedBefore),
            cancellationToken: cancellationToken);

        Log.LeaseAcquireResult(_logger, result);
        return result.ToLeaseAcquireResult();
    }

    /// <inheritdoc/>
    public async Task<LeaseAcquireResult> TryRenewLease(LeaseTicket lease, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        const string QUERY =
            /*strpsql*/"""
            SELECT o.outcome, o.id, o.token, o.expires, o.acquired, o.released
            FROM register.lease_renew(@id, @token, @now, @expires) AS o
            """;

        Guard.IsNotNull(lease);
        Guard.IsLessThan(duration, MAX_LEASE_DURATION);
        Guard.IsGreaterThan(duration, MIN_LEASE_DURATION);

        var now = _timeProvider.GetUtcNow();
        var expires = now + duration;

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await _rewnewPipeline.ExecuteAsync(
            callback: static async (s, cancellationToken) =>
            {
                var (self, conn, leaseId, token, now, expires) = s;
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await using var cmd = conn.CreateCommand(QUERY);

                cmd.Parameters.Add<string>("id", NpgsqlDbType.Text).TypedValue = leaseId;
                cmd.Parameters.Add<Guid>("token", NpgsqlDbType.Uuid).TypedValue = token;
                cmd.Parameters.Add<DateTimeOffset>("now", NpgsqlDbType.TimestampTz).TypedValue = now;
                cmd.Parameters.Add<DateTimeOffset>("expires", NpgsqlDbType.TimestampTz).TypedValue = expires;

                await cmd.PrepareAsync(cancellationToken);
                RenewResult result;

                {
                    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    var read = await reader.ReadAsync(cancellationToken);
                    Debug.Assert(read);

                    result = await RenewResult.ReadAsync(reader, cancellationToken);
                }

                if (result is RenewResult.Success)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                return result;
            },
            state: (this, conn, lease.LeaseId, lease.Token, now, expires),
            cancellationToken: cancellationToken);

        Log.LeaseRenewResult(_logger, result);
        return result.ToLeaseAcquireResult();
    }

    /// <inheritdoc/>
    public async Task<LeaseReleaseResult> ReleaseLease(LeaseTicket lease, CancellationToken cancellationToken = default)
    {
        const string QUERY =
            /*strpsql*/"""
            SELECT o.outcome, o.id, o.expires, o.acquired, o.released
            FROM register.lease_release(@id, @token, @now) AS o
            """;

        Guard.IsNotNull(lease);
        var now = _timeProvider.GetUtcNow();

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await _releasePipeline.ExecuteAsync(
            callback: static async (s, cancellationToken) =>
            {
                var (self, conn, leaseId, token, now) = s;
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await using var cmd = conn.CreateCommand(QUERY);

                cmd.Parameters.Add<string>("id", NpgsqlDbType.Text).TypedValue = leaseId;
                cmd.Parameters.Add<Guid>("token", NpgsqlDbType.Uuid).TypedValue = token;
                cmd.Parameters.Add<DateTimeOffset>("now", NpgsqlDbType.TimestampTz).TypedValue = now;

                await cmd.PrepareAsync(cancellationToken);
                ReleaseResult result;

                {
                    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    var read = await reader.ReadAsync(cancellationToken);
                    Debug.Assert(read);

                    result = await ReleaseResult.ReadAsync(reader, cancellationToken);
                }

                if (result is ReleaseResult.Success)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                return result;
            },
            state: (this, conn, lease.LeaseId, lease.Token, now),
            cancellationToken: cancellationToken);

        Log.LeaseReleaseResult(_logger, result);
        return result.ToLeaseReleaseResult();
    }

    private static ResiliencePipeline<T> CreateRetryPipeline<T>(string actionName, TimeProvider timeProvider, ILogger logger)
    {
        var pipelineBuilder = new ResiliencePipelineBuilder<T>();
        pipelineBuilder.TimeProvider = timeProvider;
        pipelineBuilder.AddRetry(new RetryStrategyOptions<T>
        {
            ShouldHandle = new PredicateBuilder<T>()
                .Handle<PostgresException>(e => e.SqlState == PostgresErrorCodes.SerializationFailure),
            BackoffType = DelayBackoffType.Constant,
            UseJitter = false,
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(10),
            OnRetry = args =>
            {
                Log.FailedToUpsertLeaseDueToSerializationError(logger, actionName, args.AttemptNumber);

                return ValueTask.CompletedTask;
            },
        });

        return pipelineBuilder.Build();
    }

    private static T UnreachableUtcome<T>(string resultName, short outcome)
    {
        throw new UnreachableException($"Unreachable outcome '{outcome}' encountered when reading {resultName}.");
    }

    private abstract record AcquireResult
    {
        public static async ValueTask<AcquireResult> ReadAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
        {
            var outcome = await reader.GetFieldValueAsync<short>("outcome", cancellationToken);
            var id = await reader.GetFieldValueAsync<string>("id", cancellationToken);
            var token = await reader.GetFieldValueAsync<Guid?>("token", cancellationToken);
            var expires = await reader.GetFieldValueAsync<DateTimeOffset?>("expires", cancellationToken);
            var acquired = await reader.GetFieldValueAsync<DateTimeOffset?>("acquired", cancellationToken);
            var released = await reader.GetFieldValueAsync<DateTimeOffset?>("released", cancellationToken);
            var condition = await reader.GetFieldValueOrDefaultAsync<string>("condition", cancellationToken);

            Debug.Assert(id is not null);
            Debug.Assert(outcome is >= 0 and <= 2);

            switch (outcome)
            {
                // Success { id: text, token: uuid, expires: timestamp with time zone, acquired: timestamp with time zone }
                case 0:
                    Debug.Assert(token.HasValue);
                    Debug.Assert(expires.HasValue);
                    Debug.Assert(acquired.HasValue);
                    return new Success(id, token.Value, expires.Value, acquired.Value);

                // ConditionUnmet { id: text, condition: text, acquired: timestamp with time zone, released: timestamp with time zone }
                case 1:
                    Debug.Assert(!string.IsNullOrEmpty(condition));
                    Debug.Assert(acquired.HasValue);
                    Debug.Assert(released.HasValue);
                    return new ConditionUnmet(id, condition, acquired.Value, released.Value);

                // LeaseUnavailable { id: text, acquired: timestamp with time zone, expires: timestamp with time zone }
                case 2:
                    Debug.Assert(acquired.HasValue);
                    Debug.Assert(expires.HasValue);
                    return new LeaseUnavailable(id, acquired.Value, expires.Value);

                default:
                    return UnreachableUtcome<AcquireResult>(nameof(AcquireResult), outcome);
            }
        }

        private AcquireResult(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public abstract LeaseAcquireResult ToLeaseAcquireResult();

        public sealed record Success(string Id, Guid Token, DateTimeOffset Expires, DateTimeOffset Acquired)
            : AcquireResult(Id)
        {
            public override LeaseAcquireResult ToLeaseAcquireResult()
                => LeaseAcquireResult.Acquired(new(Id, Token, Expires), Acquired, null);
        }

        public sealed record ConditionUnmet(string Id, string Condition, DateTimeOffset Acquired, DateTimeOffset Released)
            : AcquireResult(Id)
        {
            public override LeaseAcquireResult ToLeaseAcquireResult()
                => LeaseAcquireResult.Failed(DateTimeOffset.MinValue, Acquired, Released);
        }

        public sealed record LeaseUnavailable(string Id, DateTimeOffset Acquired, DateTimeOffset Expires)
            : AcquireResult(Id)
        {
            public override LeaseAcquireResult ToLeaseAcquireResult()
                => LeaseAcquireResult.Failed(Expires, Acquired, null);
        }
    }

    private abstract record RenewResult
    {
        public static async ValueTask<RenewResult> ReadAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
        {
            var outcome = await reader.GetFieldValueAsync<short>("outcome", cancellationToken);
            var id = await reader.GetFieldValueAsync<string>("id", cancellationToken);
            var token = await reader.GetFieldValueAsync<Guid?>("token", cancellationToken);
            var expires = await reader.GetFieldValueAsync<DateTimeOffset?>("expires", cancellationToken);
            var acquired = await reader.GetFieldValueAsync<DateTimeOffset?>("acquired", cancellationToken);
            var released = await reader.GetFieldValueAsync<DateTimeOffset?>("released", cancellationToken);

            Debug.Assert(id is not null);
            Debug.Assert(outcome is >= 0 and <= 3);

            switch (outcome)
            {
                // Success { id: text, token: uuid, expires: timestamp with time zone, acquired: timestamp with time zone }
                case 0:
                    Debug.Assert(token.HasValue);
                    Debug.Assert(expires.HasValue);
                    Debug.Assert(acquired.HasValue);
                    return new Success(id, token.Value, expires.Value, acquired.Value);

                // WrongToken { id: text, acquired: timestamp with time zone, expires: timestamp with time zone }
                case 1:
                    Debug.Assert(acquired.HasValue);
                    Debug.Assert(expires.HasValue);
                    return new WrongToken(id, acquired.Value, expires.Value);

                // LeaseNotFound { id: text }
                case 2:
                    return new LeaseNotFound(id);

                // LeaseExpired { id: text, acquired: timestamp with time zone, expires: timestamp with time zone }
                case 3:
                    Debug.Assert(acquired.HasValue);
                    Debug.Assert(expires.HasValue);
                    return new LeaseExpired(id, acquired.Value, expires.Value);

                default:
                    return UnreachableUtcome<RenewResult>(nameof(RenewResult), outcome);
            }
        }

        private RenewResult(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public abstract LeaseAcquireResult ToLeaseAcquireResult();

        public sealed record Success(string Id, Guid Token, DateTimeOffset Expires, DateTimeOffset Acquired)
            : RenewResult(Id)
        {
            public override LeaseAcquireResult ToLeaseAcquireResult()
                => LeaseAcquireResult.Acquired(new(Id, Token, Expires), Acquired, null);
        }

        public sealed record WrongToken(string Id, DateTimeOffset Acquired, DateTimeOffset Expires)
            : RenewResult(Id)
        {
            public override LeaseAcquireResult ToLeaseAcquireResult()
                => LeaseAcquireResult.Failed(Expires, Acquired, null);
        }

        public sealed record LeaseNotFound(string Id)
            : RenewResult(Id)
        {
            public override LeaseAcquireResult ToLeaseAcquireResult()
                => LeaseAcquireResult.Failed(DateTimeOffset.MinValue, null, null);
        }

        public sealed record LeaseExpired(string Id, DateTimeOffset Acquired, DateTimeOffset Expires)
            : RenewResult(Id)
        {
            public override LeaseAcquireResult ToLeaseAcquireResult()
                => LeaseAcquireResult.Failed(Expires, Acquired, null);
        }
    }

    private abstract record ReleaseResult
    {
        public static async ValueTask<ReleaseResult> ReadAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
        {
            var outcome = await reader.GetFieldValueAsync<short>("outcome", cancellationToken);
            var id = await reader.GetFieldValueAsync<string>("id", cancellationToken);
            var expires = await reader.GetFieldValueAsync<DateTimeOffset?>("expires", cancellationToken);
            var acquired = await reader.GetFieldValueAsync<DateTimeOffset?>("acquired", cancellationToken);
            var released = await reader.GetFieldValueAsync<DateTimeOffset?>("released", cancellationToken);

            Debug.Assert(id is not null);
            Debug.Assert(outcome is >= 0 and <= 3);

            switch (outcome)
            {
                // Success { id: text, acquired: timestamp with time zone, released: timestamp with time zone }
                case 0:
                    Debug.Assert(acquired.HasValue);
                    Debug.Assert(released.HasValue);
                    return new Success(id, acquired.Value, released.Value);

                // WrongToken { id: text, acquired: timestamp with time zone, expires: timestamp with time zone }
                case 1:
                    Debug.Assert(acquired.HasValue);
                    Debug.Assert(expires.HasValue);
                    return new WrongToken(id, acquired.Value, expires.Value);

                // LeaseNotFound { id: text }
                case 2:
                    return new LeaseNotFound(id);

                // LeaseExpired { id: text, acquired: timestamp with time zone, expires: timestamp with time zone }
                case 3:
                    Debug.Assert(acquired.HasValue);
                    Debug.Assert(expires.HasValue);
                    return new LeaseExpired(id, acquired.Value, expires.Value);

                default:
                    return UnreachableUtcome<ReleaseResult>(nameof(ReleaseResult), outcome);
            }
        }

        private ReleaseResult(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public abstract LeaseReleaseResult ToLeaseReleaseResult();

        public sealed record Success(string Id, DateTimeOffset Acquired, DateTimeOffset Released)
            : ReleaseResult(Id)
        {
            public override LeaseReleaseResult ToLeaseReleaseResult()
                => new LeaseReleaseResult { IsReleased = true, Expires = DateTimeOffset.MinValue, LastAcquiredAt = Acquired, LastReleasedAt = Released };
        }

        public sealed record WrongToken(string Id, DateTimeOffset Acquired, DateTimeOffset Expires)
            : ReleaseResult(Id)
        {
            public override LeaseReleaseResult ToLeaseReleaseResult()
                => new LeaseReleaseResult { IsReleased = false, Expires = Expires, LastAcquiredAt = Acquired, LastReleasedAt = null };
        }

        public sealed record LeaseNotFound(string Id)
            : ReleaseResult(Id)
        {
            public override LeaseReleaseResult ToLeaseReleaseResult()
                => new LeaseReleaseResult { IsReleased = false, Expires = DateTimeOffset.MinValue, LastAcquiredAt = null, LastReleasedAt = null };
        }

        public sealed record LeaseExpired(string Id, DateTimeOffset Acquired, DateTimeOffset Expires)
            : ReleaseResult(Id)
        {
            public override LeaseReleaseResult ToLeaseReleaseResult()
                => new LeaseReleaseResult { IsReleased = false, Expires = Expires, LastAcquiredAt = Acquired, LastReleasedAt = null };
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Debug, "Failed to {Action} lease due to serialization error. Attempt = {AttemptNumber}")]
        public static partial void FailedToUpsertLeaseDueToSerializationError(ILogger logger, string action, int attemptNumber);

        [LoggerMessage(6, LogLevel.Debug, "Lease {LeaseId} acquire result: {Result}")]
        private static partial void LeaseAcquireResult(ILogger logger, string leaseId, AcquireResult result);

        public static void LeaseAcquireResult(ILogger logger, AcquireResult result) => LeaseAcquireResult(logger, result.Id, result);

        [LoggerMessage(7, LogLevel.Debug, "Lease {LeaseId} renew result: {Result}")]
        private static partial void LeaseRenewResult(ILogger logger, string leaseId, RenewResult result);

        public static void LeaseRenewResult(ILogger logger, RenewResult result) => LeaseRenewResult(logger, result.Id, result);

        [LoggerMessage(8, LogLevel.Debug, "Lease {LeaseId} release result: {Result}")]
        private static partial void LeaseReleaseResult(ILogger logger, string leaseId, ReleaseResult result);

        public static void LeaseReleaseResult(ILogger logger, ReleaseResult result) => LeaseReleaseResult(logger, result.Id, result);
    }
}
