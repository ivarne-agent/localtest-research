using System.Buffers;
using System.Data;
using System.Text.Json;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ServiceDefaults.Npgsql;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.UnitOfWork;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Register.Persistence.ImportJobs;

/// <summary>
/// Postgres backed implementation of <see cref="IImportJobStatePersistence"/>.
/// </summary>
internal sealed class PostgresImportJobStatePersistence
    : IImportJobStatePersistence
{
    private static readonly JsonSerializerOptions _options = JsonSerializerOptions.Web;

    private static readonly JsonWriterOptions _writerOptions = new JsonWriterOptions
    {
        Encoder = _options.Encoder,
        Indented = _options.WriteIndented,
        IndentCharacter = _options.IndentCharacter,
        IndentSize = _options.IndentSize,
        MaxDepth = _options.MaxDepth,
        NewLine = _options.NewLine,
    };

    private static readonly JsonReaderOptions _readerOptions = new JsonReaderOptions
    {
        AllowTrailingCommas = _options.AllowTrailingCommas,
        CommentHandling = _options.ReadCommentHandling,
        MaxDepth = _options.MaxDepth,
    };

    private readonly IUnitOfWorkHandle _handle;
    private readonly NpgsqlConnection _connection;
    private readonly ILogger<PostgresImportJobStatePersistence> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresImportJobStatePersistence"/> class.
    /// </summary>
    public PostgresImportJobStatePersistence(
        IUnitOfWorkHandle handle,
        NpgsqlConnection connection,
        ILogger<PostgresImportJobStatePersistence> logger)
    {
        _handle = handle;
        _connection = connection;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<FieldValue<T>> GetState<T>(string jobId, CancellationToken cancellationToken = default)
        where T : IImportJobState<T>
    {
        const string QUERY =
            /*strpsql*/"""
            SELECT state_type, state_value
              FROM register.import_job_state
             WHERE job_id = @jobId
            FOR NO KEY UPDATE
            """;

        _handle.ThrowIfCompleted();

        await using var cmd = _connection.CreateCommand(QUERY);

        cmd.Parameters.Add<string>("jobId", NpgsqlDbType.Text).TypedValue = jobId;

        await cmd.PrepareAsync(cancellationToken);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return FieldValue<T>.Unset;
        }

        var type = await reader.GetFieldValueAsync<string>("state_type", cancellationToken);
        await using var stream = await reader.GetFieldValueAsync<Stream>("state_value", cancellationToken);

        using var seq = new Sequence<byte>(arrayPool: ArrayPool<byte>.Shared);
        await stream.CopyToAsync(seq.AsStream(), cancellationToken);

        return ReadFrom<T>(seq.AsReadOnlySequence, type) switch
        {
            null => FieldValue<T>.Null,
            T state => state,
        };
    }

    /// <inheritdoc/>
    public async Task SetState<T>(string jobId, T state, CancellationToken cancellationToken = default)
        where T : IImportJobState<T>
    {
        const string QUERY =
            /*strpsql*/"""
            INSERT INTO register.import_job_state (job_id, state_type, state_value)
            VALUES (@jobId, @stateType, @stateValue)
            ON CONFLICT (job_id) DO UPDATE
                SET state_type = @stateType
                  , state_value = @stateValue
            """;

        _handle.ThrowIfCompleted();

        await using var cmd = _connection.CreateCommand(QUERY);

        cmd.Parameters.Add<string>("jobId", NpgsqlDbType.Text).TypedValue = jobId;
        cmd.Parameters.Add<string>("stateType", NpgsqlDbType.Text).TypedValue = T.StateType;
        var stateValueParam = cmd.Parameters.Add<Stream>("stateValue", NpgsqlDbType.Jsonb);

        await cmd.PrepareAsync(cancellationToken);

        using var seq = new Sequence<byte>(arrayPool: ArrayPool<byte>.Shared);
        WriteTo(seq, state);

        stateValueParam.TypedValue = seq.AsReadOnlySequence.AsStream();
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ClearState(string jobId, CancellationToken cancellationToken = default)
    {
        const string QUERY =
            /*strpsql*/"""
            DELETE FROM register.import_job_state
             WHERE job_id = @jobId
            """;

        _handle.ThrowIfCompleted();

        await using var cmd = _connection.CreateCommand(QUERY);

        cmd.Parameters.Add<string>("jobId", NpgsqlDbType.Text).TypedValue = jobId;

        await cmd.PrepareAsync(cancellationToken);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<FieldValue<T>> GetPartyState<T>(string jobId, Guid partyUuid, CancellationToken cancellationToken = default)
        where T : IImportJobState<T>
    {
        const string QUERY =
            /*strpsql*/"""
            SELECT state_type, state_value
              FROM register.import_job_party_state
             WHERE job_id = @jobId
               AND party_uuid = @partyUuid
            FOR NO KEY UPDATE
            """;

        _handle.ThrowIfCompleted();

        await using var cmd = _connection.CreateCommand(QUERY);

        cmd.Parameters.Add<string>("jobId", NpgsqlDbType.Text).TypedValue = jobId;
        cmd.Parameters.Add<Guid>("partyUuid", NpgsqlDbType.Uuid).TypedValue = partyUuid;

        await cmd.PrepareAsync(cancellationToken);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return FieldValue<T>.Unset;
        }

        var type = await reader.GetFieldValueAsync<string>("state_type", cancellationToken);
        await using var stream = await reader.GetFieldValueAsync<Stream>("state_value", cancellationToken);

        using var seq = new Sequence<byte>(arrayPool: ArrayPool<byte>.Shared);
        await stream.CopyToAsync(seq.AsStream(), cancellationToken);

        return ReadFrom<T>(seq.AsReadOnlySequence, type) switch
        {
            null => FieldValue<T>.Null,
            T state => state,
        };
    }

    /// <inheritdoc/>
    public async Task SetPartyState<T>(string jobId, Guid partyUuid, T state, CancellationToken cancellationToken = default)
        where T : IImportJobState<T>
    {
        const string QUERY =
            /*strpsql*/"""
            INSERT INTO register.import_job_party_state (job_id, party_uuid, state_type, state_value)
            VALUES (@jobId, @partyUuid, @stateType, @stateValue)
            ON CONFLICT (job_id, party_uuid) DO UPDATE
                SET state_type = @stateType
                  , state_value = @stateValue
            """;

        _handle.ThrowIfCompleted();

        await using var cmd = _connection.CreateCommand(QUERY);

        cmd.Parameters.Add<string>("jobId", NpgsqlDbType.Text).TypedValue = jobId;
        cmd.Parameters.Add<Guid>("partyUuid", NpgsqlDbType.Uuid).TypedValue = partyUuid;
        cmd.Parameters.Add<string>("stateType", NpgsqlDbType.Text).TypedValue = T.StateType;
        var stateValueParam = cmd.Parameters.Add<Stream>("stateValue", NpgsqlDbType.Jsonb);

        await cmd.PrepareAsync(cancellationToken);

        using var seq = new Sequence<byte>(arrayPool: ArrayPool<byte>.Shared);
        WriteTo(seq, state);

        stateValueParam.TypedValue = seq.AsReadOnlySequence.AsStream();
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ClearPartyState(string jobId, Guid partyUuid, CancellationToken cancellationToken = default)
    {
        const string QUERY =
            /*strpsql*/"""
            DELETE FROM register.import_job_party_state
             WHERE job_id = @jobId
               AND party_uuid = @partyUuid
            """;

        _handle.ThrowIfCompleted();

        await using var cmd = _connection.CreateCommand(QUERY);

        cmd.Parameters.Add<string>("jobId", NpgsqlDbType.Text).TypedValue = jobId;
        cmd.Parameters.Add<Guid>("partyUuid", NpgsqlDbType.Uuid).TypedValue = partyUuid;

        await cmd.PrepareAsync(cancellationToken);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void WriteTo<T>(Sequence<byte> seq, T state)
        where T : IImportJobState<T>
    {
        using var writer = new Utf8JsonWriter(seq, _writerOptions);
        JsonSerializer.Serialize(writer, state, _options);
        writer.Flush();
    }

    private static T? ReadFrom<T>(ReadOnlySequence<byte> seq, string stateType)
        where T : IImportJobState<T>
    {
        if (seq.Length <= 0)
        {
            return default;
        }

        var versionByte = seq.First.Span[0];
        seq = seq.Slice(1);

        return versionByte switch
        {
            1 => ReadFromV1<T>(seq, stateType),
            _ => ThrowHelper.ThrowNotSupportedException<T?>($"Unsupported JSONB version byte received: {versionByte}"),
        };
    }

    private static T? ReadFromV1<T>(ReadOnlySequence<byte> seq, string stateType)
        where T : IImportJobState<T>
    {
        var reader = new Utf8JsonReader(seq, _readerOptions);

        return T.Read(ref reader, stateType, _options);
    }
}
