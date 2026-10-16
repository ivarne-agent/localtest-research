using System.Buffers;
using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Altinn.Authorization.ServiceDefaults.Npgsql;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.UnitOfWork;
using CommunityToolkit.Diagnostics;
using Nerdbank.Streams;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Register.Persistence.ImportJobs;

/// <summary>
/// Postgres backed implementation of <see cref="ISagaStatePersistence"/>.
/// </summary>
internal sealed class PostgresSagaStatePersistence
    : ISagaStatePersistence
    , ISagaStateCleanup
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerOptions.Web)
    {
        // JsonB in postgres does not retain property order
        AllowOutOfOrderMetadataProperties = true,
    };

    /// <summary>
    /// Expose the json serializer options used for testing.
    /// </summary>
    internal static JsonSerializerOptions JsonSerializerOptions => _options;

    private static readonly JsonWriterOptions _writerOptions = new JsonWriterOptions
    {
        Encoder = _options.Encoder,
        Indented = _options.WriteIndented,
        IndentCharacter = _options.IndentCharacter,
        IndentSize = _options.IndentSize,
        MaxDepth = _options.MaxDepth,
        NewLine = _options.NewLine,
    };

    private readonly IUnitOfWorkHandle _handle;
    private readonly NpgsqlConnection _connection;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresSagaStatePersistence"/> class.
    /// </summary>
    public PostgresSagaStatePersistence(
        IUnitOfWorkHandle handle,
        NpgsqlConnection connection,
        TimeProvider timeProvider)
    {
        _handle = handle;
        _connection = connection;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<SagaState<T>> GetState<T>(Guid sagaId, CancellationToken cancellationToken = default)
        where T : class, ISagaStateData<T>
    {
        const string QUERY =
            /*strpsql*/"""
            SELECT s.status, s.state_type, s.message_ids, s.state_value
            FROM  register.saga_state s
            WHERE s.id = @id
            FOR NO KEY UPDATE
            """;

        _handle.ThrowIfCompleted();

        await using var cmd = _connection.CreateCommand(QUERY);

        cmd.Parameters.Add<Guid>("id", NpgsqlDbType.Uuid).TypedValue = sagaId;

        await cmd.PrepareAsync(cancellationToken);

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult | CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new(sagaId, SagaStatus.InProgress, null, []);
        }

        var status = await reader.GetFieldValueAsync<SagaStatus>("status", cancellationToken);
        var type = await reader.GetFieldValueAsync<string>("state_type", cancellationToken);
        var messages = await reader.GetFieldValueAsync<Guid[]>("message_ids", cancellationToken);
        await using var stream = await reader.GetFieldValueAsync<Stream>("state_value", cancellationToken);

        return await ReadFrom<T>(stream, type, cancellationToken) switch
        {
            null => throw new InvalidOperationException($"State in database is not deserializable to saga state of type '{typeof(T)}'"),
            T state => new(sagaId, status, state, messages),
        };
    }

    /// <inheritdoc/>
    public async Task SaveState<T>(SagaState<T> state, CancellationToken cancellationToken = default)
        where T : class, ISagaStateData<T>
    {
        const string QUERY =
            /*strpsql*/"""
            INSERT INTO register.saga_state ("id", "status", message_ids, state_type, state_value, created, updated)
            VALUES (@id, @status, @messageIds, @stateType, @stateValue, @now, @now)
            ON CONFLICT ("id")
            DO UPDATE
                 SET "status"    = EXCLUDED."status"
                   , message_ids = EXCLUDED.message_ids
                   , state_type  = EXCLUDED.state_type
                   , state_value = EXCLUDED.state_value
                   , updated     = EXCLUDED.updated
            """;

        _handle.ThrowIfCompleted();

        if (state.Data is null)
        {
            throw new ArgumentException($"{nameof(SagaState<>.Data)} cannot be null", nameof(state));
        }

        await using var cmd = _connection.CreateCommand(QUERY);

        cmd.Parameters.Add<DateTimeOffset>("now", NpgsqlDbType.TimestampTz).TypedValue = _timeProvider.GetUtcNow();
        cmd.Parameters.Add<Guid>("id", NpgsqlDbType.Uuid).TypedValue = state.SagaId;
        cmd.Parameters.Add<SagaStatus>("status").TypedValue = state.Status;
        cmd.Parameters.Add<string>("stateType", NpgsqlDbType.Text).TypedValue = T.StateType;
        cmd.Parameters.Add<List<Guid>>("messageIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).TypedValue = [.. state.Messages];
        var stateValueParam = cmd.Parameters.Add<Stream>("stateValue", NpgsqlDbType.Jsonb);

        await cmd.PrepareAsync(cancellationToken);

        using var seq = new Sequence<byte>(arrayPool: ArrayPool<byte>.Shared);
        WriteTo(seq, state.Data);

        stateValueParam.TypedValue = seq.AsReadOnlySequence.AsStream();
        var updated = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (updated == 0)
        {
            throw new UnreachableException($"No records were updated - this should not happen. Saga id = '{state.SagaId}'");
        }
    }

    /// <inheritdoc/>
    public async Task DeleteState(Guid sagaId, CancellationToken cancellationToken = default)
    {
        const string QUERY =
            /*strpsql*/"""
            DELETE FROM register.saga_state
             WHERE id = @id
            """;

        _handle.ThrowIfCompleted();

        await using var cmd = _connection.CreateCommand(QUERY);

        cmd.Parameters.Add<Guid>("id", NpgsqlDbType.Uuid).TypedValue = sagaId;

        await cmd.PrepareAsync(cancellationToken);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteOldStates(
        DateTimeOffset? completedBefore = null,
        DateTimeOffset? faultedBefore = null,
        DateTimeOffset? inProgressBefore = null,
        CancellationToken cancellationToken = default)
    {
        const string QUERY =
            /*strpsql*/"""
            DELETE FROM register.saga_state s
             WHERE (s.updated < @completedBefore AND s.status = 'completed')
                OR (s.updated < @faultedBefore AND s.status = 'faulted')
                OR (s.updated < @inProgressBefore AND s.status = 'in_progress')
            """;

        if (completedBefore is null && faultedBefore is null && inProgressBefore is null)
        {
            ThrowHelper.ThrowArgumentException(nameof(completedBefore), "At least one of the parameters must be provided");
        }

        _handle.ThrowIfCompleted();

        await using var cmd = _connection.CreateCommand(QUERY);

        cmd.Parameters.Add<DateTimeOffset>("completedBefore", NpgsqlDbType.TimestampTz).TypedValue = completedBefore ?? DateTimeOffset.UnixEpoch;
        cmd.Parameters.Add<DateTimeOffset>("faultedBefore", NpgsqlDbType.TimestampTz).TypedValue = faultedBefore ?? DateTimeOffset.UnixEpoch;
        cmd.Parameters.Add<DateTimeOffset>("inProgressBefore", NpgsqlDbType.TimestampTz).TypedValue = inProgressBefore ?? DateTimeOffset.UnixEpoch;

        await cmd.PrepareAsync(cancellationToken);
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void WriteTo<T>(Sequence<byte> seq, T state)
        where T : class, ISagaStateData<T>
    {
        using var writer = new Utf8JsonWriter(seq, _writerOptions);
        JsonSerializer.Serialize(writer, state, _options);
        writer.Flush();
    }

    private static ValueTask<T?> ReadFrom<T>(Stream stream, string stateType, CancellationToken cancellationToken)
        where T : class, ISagaStateData<T>
    {
        var versionByte = stream.ReadByte();
        return versionByte switch
        {
            -1 => ValueTask.FromResult<T?>(default),
            1 => ReadFromV1<T>(stream, stateType, cancellationToken),
            _ => ThrowHelper.ThrowNotSupportedException<ValueTask<T?>>($"Unsupported JSONB version byte received: {versionByte}"),
        };
    }

    private static ValueTask<T?> ReadFromV1<T>(Stream stream, string stateType, CancellationToken cancellationToken)
        where T : ISagaStateData<T>
        => T.ReadAsync(stream, stateType, _options, cancellationToken);
}
