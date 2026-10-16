using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts;
using Altinn.Register.Contracts.ExternalRoles;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.Core.Utils;
using Altinn.Register.Persistence.AsyncEnumerables;
using Altinn.Register.Persistence.DbArgTypes;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Register.Persistence;

/// <summary>
/// Implementation of <see cref="IV1PartyService"/> backed by a PostgreSQL database.
/// </summary>
internal partial class PostgreSqlPartyPersistence
    : IPartyPersistence
    , IPartyExternalRolePersistence
{
    private readonly IUnitOfWorkHandle _handle;
    private readonly NpgsqlConnection _connection;
    private readonly TimeProvider _timeProvider;
    private readonly PersistenceFeatureFlag[] _flags;
    private readonly ILogger<PostgreSqlPartyPersistence> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlPartyPersistence"/> class.
    /// </summary>
    public PostgreSqlPartyPersistence(
        IUnitOfWorkHandle handle,
        NpgsqlConnection connection,
        TimeProvider timeProvider,
        IConfiguration configuration,
        ILogger<PostgreSqlPartyPersistence> logger)
    {
        _handle = handle;
        _connection = connection;
        _timeProvider = timeProvider;
        _logger = logger;

        _flags = PersistenceFeatureFlag.FromConfiguration(configuration);
    }

    #region Party

    /// <inheritdoc/>
    public IAsyncEnumerable<PartyRecord> GetPartyById(
        Guid partyUuid,
        PartyFieldIncludes include = PartyFieldIncludes.Party,
        CancellationToken cancellationToken = default)
    {
        _handle.ThrowIfCompleted();

        // always include by-field in the result
        include |= PartyFieldIncludes.PartyUuid;

        var query = PartyQuery.Get(include, PartyQueryFilters.LookupOne(PartyLookupIdentifiers.PartyUuid));

        NpgsqlCommand? cmd = null;
        try
        {
            cmd = _connection.CreateCommand();
            cmd.CommandText = query.CommandText;

            query.AddPartyUuidParameter(cmd, partyUuid);

            return PrepareAndReadPartiesAsync(cmd, query, cancellationToken);
        }
        catch (Exception e)
        {
            return new ThrowingAsyncEnumerable<PartyRecord>(e).Using(cmd);
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<PartyRecord> GetPartyById(
        uint partyId,
        PartyFieldIncludes include = PartyFieldIncludes.Party,
        CancellationToken cancellationToken = default)
    {
        _handle.ThrowIfCompleted();

        // always include by-field in the result
        include |= PartyFieldIncludes.PartyId;

        var query = PartyQuery.Get(include, PartyQueryFilters.LookupOne(PartyLookupIdentifiers.PartyId));

        NpgsqlCommand? cmd = null;
        try
        {
            cmd = _connection.CreateCommand();
            cmd.CommandText = query.CommandText;

            query.AddPartyIdParameter(cmd, partyId);

            return PrepareAndReadPartiesAsync(cmd, query, cancellationToken);
        }
        catch (Exception e)
        {
            return new ThrowingAsyncEnumerable<PartyRecord>(e).Using(cmd);
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<OrganizationRecord> GetOrganizationByIdentifier(
        OrganizationIdentifier identifier,
        PartyFieldIncludes include = PartyFieldIncludes.Party,
        CancellationToken cancellationToken = default)
    {
        _handle.ThrowIfCompleted();

        // always include by-field in the result
        include |= PartyFieldIncludes.PartyOrganizationIdentifier;

        // filter out person fields as result is guaranteed to be an organization
        include &= ~PartyFieldIncludes.Person;

        var query = PartyQuery.Get(include, PartyQueryFilters.LookupOne(PartyLookupIdentifiers.OrganizationIdentifier));
        NpgsqlCommand? cmd = null;
        try
        {
            cmd = _connection.CreateCommand();
            cmd.CommandText = query.CommandText;

            query.AddOrganizationIdentifierParameter(cmd, identifier.ToString());

            return PrepareAndReadPartiesAsync(cmd, query, cancellationToken).Cast<OrganizationRecord>();
        }
        catch (Exception e)
        {
            return new ThrowingAsyncEnumerable<OrganizationRecord>(e).Using(cmd);
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<PersonRecord> GetPersonByIdentifier(
        PersonIdentifier identifier,
        PartyFieldIncludes include = PartyFieldIncludes.Party,
        CancellationToken cancellationToken = default)
    {
        _handle.ThrowIfCompleted();

        // always include by-field in the result
        include |= PartyFieldIncludes.PartyPersonIdentifier;

        // filter out organization fields as result is guaranteed to be a person
        include &= ~PartyFieldIncludes.Organization;

        var query = PartyQuery.Get(include, PartyQueryFilters.LookupOne(PartyLookupIdentifiers.PersonIdentifier));
        NpgsqlCommand? cmd = null;
        try
        {
            cmd = _connection.CreateCommand();
            cmd.CommandText = query.CommandText;

            query.AddPersonIdentifierParameter(cmd, identifier.ToString());

            return PrepareAndReadPartiesAsync(cmd, query, cancellationToken).Cast<PersonRecord>();
        }
        catch (Exception e)
        {
            return new ThrowingAsyncEnumerable<PersonRecord>(e).Using(cmd);
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<PartyRecord> GetPartyByUserId(
        uint userId,
        PartyFieldIncludes include = PartyFieldIncludes.Party,
        CancellationToken cancellationToken = default)
    {
        _handle.ThrowIfCompleted();

        // always include by-field in the result
        include |= PartyFieldIncludes.UserId;

        var query = PartyQuery.Get(include, PartyQueryFilters.LookupOne(PartyLookupIdentifiers.UserId));
        NpgsqlCommand? cmd = null;
        try
        {
            cmd = _connection.CreateCommand();
            cmd.CommandText = query.CommandText;

            query.AddUserIdParameter(cmd, checked((int)userId));

            return PrepareAndReadPartiesAsync(cmd, query, cancellationToken);
        }
        catch (Exception e)
        {
            return new ThrowingAsyncEnumerable<PartyRecord>(e).Using(cmd);
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<PartyRecord> LookupParties(
        IReadOnlyList<Guid>? partyUuids = null,
        IReadOnlyList<uint>? partyIds = null,
        IReadOnlyList<PartyExternalRefUrn>? externalUrns = null,
        IReadOnlyList<OrganizationIdentifier>? organizationIdentifiers = null,
        IReadOnlyList<PersonIdentifier>? personIdentifiers = null,
        IReadOnlyList<uint>? userIds = null,
        IReadOnlyList<string>? usernames = null,
        IReadOnlyList<string>? selfIdentifiedEmails = null,
        PartyFieldIncludes include = PartyFieldIncludes.Party,
        PartyListTransforms transforms = PartyListTransforms.None,
        CancellationToken cancellationToken = default)
    {
        _handle.ThrowIfCompleted();

        bool any = false, orgs = false, persons = false;
        PartyLookupIdentifiers identifiers = PartyLookupIdentifiers.None;

        if (partyUuids is { Count: > 0 })
        {
            any = orgs = persons = true;
            identifiers |= PartyLookupIdentifiers.PartyUuid;
            include |= PartyFieldIncludes.PartyUuid;
        }

        if (partyIds is { Count: > 0 })
        {
            any = orgs = persons = true;
            identifiers |= PartyLookupIdentifiers.PartyId;
            include |= PartyFieldIncludes.PartyId;
        }

        if (externalUrns is { Count: > 0 })
        {
            any = orgs = persons = true;
            identifiers |= PartyLookupIdentifiers.ExternalUrn;
            include |= PartyFieldIncludes.PartyExternalUrn;
        }

        if (organizationIdentifiers is { Count: > 0 })
        {
            any = orgs = true;
            identifiers |= PartyLookupIdentifiers.OrganizationIdentifier;
            include |= PartyFieldIncludes.PartyOrganizationIdentifier;
        }

        if (personIdentifiers is { Count: > 0 })
        {
            any = persons = true;
            identifiers |= PartyLookupIdentifiers.PersonIdentifier;
            include |= PartyFieldIncludes.PartyPersonIdentifier;
        }

        if (userIds is { Count: > 0 })
        {
            any = persons = true;
            identifiers |= PartyLookupIdentifiers.UserId;
            include |= PartyFieldIncludes.UserId;
        }

        if (usernames is { Count: > 0 })
        {
            any = persons = true;
            identifiers |= PartyLookupIdentifiers.Username;
            include |= PartyFieldIncludes.Username;
        }

        if (selfIdentifiedEmails is { Count: > 0 })
        {
            any = persons = true;
            identifiers |= PartyLookupIdentifiers.SelfIdentifiedEmail;
            include |= PartyFieldIncludes.SelfIdentifiedUserEmail;
        }

        if (!any)
        {
            return AsyncEnumerable.Empty<PartyRecord>();
        }

        if (!orgs)
        {
            // filter out organization fields as result is guaranteed to not be organizations
            include &= ~PartyFieldIncludes.Organization;
            transforms &= ~PartyListTransforms.IncludeSubUnits;
        }

        if (!persons)
        {
            // filter out person fields as result is guaranteed to not be persons
            include &= ~PartyFieldIncludes.Person;
        }

        var query = PartyQuery.Get(include, PartyQueryFilters.Lookup(identifiers, transforms));
        NpgsqlCommand? cmd = null;
        try
        {
            cmd = _connection.CreateCommand();
            cmd.CommandText = query.CommandText;

            if (partyUuids is { Count: > 0 })
            {
                query.AddPartyUuidListParameter(cmd, [.. partyUuids]);
            }

            if (partyIds is { Count: > 0 })
            {
                query.AddPartyIdListParameter(cmd, [.. partyIds.Select(static id => checked((int)id))]);
            }

            if (externalUrns is { Count: > 0 })
            {
                query.AddExternalUrnListParameter(cmd, [.. externalUrns.Select(static o => o.Urn)]);
            }

            if (organizationIdentifiers is { Count: > 0 })
            {
                query.AddOrganizationIdentifierListParameter(cmd, [.. organizationIdentifiers.Select(static o => o.ToString())]);
            }

            if (personIdentifiers is { Count: > 0 })
            {
                query.AddPersonIdentifierListParameter(cmd, [.. personIdentifiers.Select(static p => p.ToString())]);
            }

            if (userIds is { Count: > 0 })
            {
                query.AddUserIdListParameter(cmd, [.. userIds.Select(static id => checked((int)id))]);
            }

            if (usernames is { Count: > 0 })
            {
                query.AddUsernameListParameter(cmd, [.. usernames]);
            }

            if (selfIdentifiedEmails is { Count: > 0 })
            {
                query.AddSelfIdentifiedEmailListParameter(cmd, [.. selfIdentifiedEmails]);
            }

            return PrepareAndReadPartiesAsync(cmd, query, cancellationToken);
        }
        catch (Exception e)
        {
            return new ThrowingAsyncEnumerable<PartyRecord>(e).Using(cmd);
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<PartyRecord> GetPartyStream(
        ulong fromExclusive,
        ushort limit,
        PartyFieldIncludes include = PartyFieldIncludes.Party,
        IReadOnlySet<PartyRecordType>? filterByPartyType = null,
        CancellationToken cancellationToken = default)
    {
        _handle.ThrowIfCompleted();

        var filter = PartyListFilters.None;
        if (filterByPartyType is { Count: > 0 })
        {
            filter |= PartyListFilters.PartyType;
        }

        var query = PartyQuery.Get(include, PartyQueryFilters.Stream(filter));
        NpgsqlCommand? cmd = null;
        try
        {
            cmd = _connection.CreateCommand();
            cmd.CommandText = query.CommandText;

            query.AddStreamPageParameters(cmd, fromExclusive, limit);

            if (filterByPartyType is not null)
            {
                query.AddPartyTypeListParameter(cmd, [.. filterByPartyType]);
            }

            return PrepareAndReadPartiesAsync(cmd, query, cancellationToken);
        }
        catch (Exception e)
        {
            return new ThrowingAsyncEnumerable<PartyRecord>(e).Using(cmd);
        }
    }

    /// <inheritdoc/>
    public async Task<ulong> GetMaxPartyVersionId(CancellationToken cancellationToken)
    {
        const string QUERY =
            /*strpsql*/"""
            SELECT MAX(version_id) FROM register.party
            """;

        _handle.ThrowIfCompleted();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = QUERY;

        await cmd.PrepareAsync(cancellationToken);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            ThrowHelper.ThrowInvalidOperationException("No rows returned from MAX(version_id) query");
        }

        if (await reader.IsDBNullAsync(0, cancellationToken))
        {
            return 0;
        }

        return (ulong)await reader.GetFieldValueAsync<long>(0, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<PartyRecord>> UpsertParty(
        PartyRecord party,
        CancellationToken cancellationToken = default)
    {
        _handle.ThrowIfCompleted();

        return UpsertPartyQuery.UpsertParty(_connection, party, _flags, cancellationToken).AsTask();
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<Result<PartyRecord>> UpsertParties(
        IAsyncEnumerable<PartyRecord> parties,
        CancellationToken cancellationToken = default)
    {
        _handle.ThrowIfCompleted();

        return UpsertPartyQuery.UpsertParties(_connection, parties, _flags, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<NewOrExisting<SelfIdentifiedUserRecord>>> GetOrCreateSelfIdentifiedEmailUser(
        string email,
        CancellationToken cancellationToken = default)
    {
        _handle.ThrowIfCompleted();

        var now = _timeProvider.GetUtcNow();
        return UpsertPartyQuery.GetOrCreateSelfIdentifiedEmailUser(
            _connection,
            email,
            now,
            _flags,
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<NewOrExisting<SelfIdentifiedUserRecord>>> GetOrCreateSelfIdentifiedEduUser(
        string extRef,
        string username,
        CancellationToken cancellationToken = default)
    {
        _handle.ThrowIfCompleted();

        var now = _timeProvider.GetUtcNow();
        return UpsertPartyQuery.GetOrCreateSelfIdentifiedEduUser(
            _connection,
            extRef,
            username,
            now,
            _flags,
            cancellationToken);
    }

    private IAsyncEnumerable<PartyRecord> PrepareAndReadPartiesAsync(
        NpgsqlCommand cmd,
        PartyQuery query,
        CancellationToken cancellationToken)
    {
        Guard.IsNotNull(cmd);
        Guard.IsNotNull(query);

        return query.PrepareAndReadPartiesAsync(cmd, cancellationToken);
    }

    #endregion

    #region Roles

    /// <inheritdoc/>
    IAsyncEnumerable<PartyExternalRoleAssignmentRecord> IPartyExternalRolePersistence.GetExternalRoleAssignmentsFromParty(
        Guid partyUuid,
        PartyExternalRoleAssignmentFieldIncludes include,
        CancellationToken cancellationToken)
        => GetExternalRoleAssignmentsFromParty(partyUuid, null, include, cancellationToken);

    /// <inheritdoc/>
    IAsyncEnumerable<PartyExternalRoleAssignmentRecord> IPartyExternalRolePersistence.GetExternalRoleAssignmentsFromParty(
        Guid partyUuid,
        ExternalRoleReference role,
        PartyExternalRoleAssignmentFieldIncludes include,
        CancellationToken cancellationToken)
        => GetExternalRoleAssignmentsFromParty(partyUuid, role, include, cancellationToken);

    /// <inheritdoc/>
    async IAsyncEnumerable<PartyExternalRoleAssignmentRecord> IPartyExternalRolePersistence.GetExternalRoleAssignmentsFromParty(
        Guid partyUuid,
        IReadOnlyList<ExternalRoleReference> roles,
        PartyExternalRoleAssignmentFieldIncludes include,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // TODO: Update the method below to support multiple roles instead of looping
        foreach (var role in roles.Distinct())
        {
            await foreach (var assignment in GetExternalRoleAssignmentsFromParty(partyUuid, role, include, cancellationToken))
            {
                yield return assignment;
            }
        }
    }

    /// <inheritdoc cref="IPartyExternalRolePersistence.GetExternalRoleAssignmentsFromParty(Guid, ExternalRoleReference, PartyExternalRoleAssignmentFieldIncludes, CancellationToken)"/>
    public IAsyncEnumerable<PartyExternalRoleAssignmentRecord> GetExternalRoleAssignmentsFromParty(
        Guid partyUuid,
        ExternalRoleReference? role = null,
        PartyExternalRoleAssignmentFieldIncludes include = PartyExternalRoleAssignmentFieldIncludes.RoleAssignment,
        CancellationToken cancellationToken = default)
    {
        _handle.ThrowIfCompleted();

        var filter = PartyRoleFilters.FromParty;
        if (role is not null)
        {
            filter |= PartyRoleFilters.Role;
        }

        var query = PartyRoleQuery.Get(include, filter);
        NpgsqlCommand? cmd = null;
        try
        {
            cmd = _connection.CreateCommand();
            cmd.CommandText = query.CommandText;

            query.AddFromPartyParameter(cmd, partyUuid);

            if (role is not null)
            {
                query.AddRoleSourceParameter(cmd, role.Source);
                query.AddRoleIdentifierParameter(cmd, role.Identifier);
            }

            return PrepareAndReadPartyRolesAsync(cmd, query, cancellationToken);
        }
        catch (Exception e)
        {
            return new ThrowingAsyncEnumerable<PartyExternalRoleAssignmentRecord>(e).Using(cmd);
        }
    }

    /// <inheritdoc/>
    IAsyncEnumerable<PartyExternalRoleAssignmentRecord> IPartyExternalRolePersistence.GetExternalRoleAssignmentsToParty(
        Guid partyUuid,
        PartyExternalRoleAssignmentFieldIncludes include,
        CancellationToken cancellationToken)
        => GetExternalRoleAssignmentsToParty(partyUuid, null, include, cancellationToken);

    /// <inheritdoc/>
    IAsyncEnumerable<PartyExternalRoleAssignmentRecord> IPartyExternalRolePersistence.GetExternalRoleAssignmentsToParty(
        Guid partyUuid,
        ExternalRoleReference role,
        PartyExternalRoleAssignmentFieldIncludes include,
        CancellationToken cancellationToken)
        => GetExternalRoleAssignmentsToParty(partyUuid, role, include, cancellationToken);

    /// <inheritdoc cref="IPartyExternalRolePersistence.GetExternalRoleAssignmentsToParty(Guid, ExternalRoleReference, PartyExternalRoleAssignmentFieldIncludes, CancellationToken)"/>
    public IAsyncEnumerable<PartyExternalRoleAssignmentRecord> GetExternalRoleAssignmentsToParty(
        Guid partyUuid,
        ExternalRoleReference? role = null,
        PartyExternalRoleAssignmentFieldIncludes include = PartyExternalRoleAssignmentFieldIncludes.RoleAssignment,
        CancellationToken cancellationToken = default)
    {
        _handle.ThrowIfCompleted();

        var filter = PartyRoleFilters.ToParty;
        if (role is not null)
        {
            filter |= PartyRoleFilters.Role;
        }

        var query = PartyRoleQuery.Get(include, filter);
        NpgsqlCommand? cmd = null;
        try
        {
            cmd = _connection.CreateCommand();
            cmd.CommandText = query.CommandText;

            query.AddToPartyParameter(cmd, partyUuid);

            if (role is not null)
            {
                query.AddRoleSourceParameter(cmd, role.Source);
                query.AddRoleIdentifierParameter(cmd, role.Identifier);
            }

            return PrepareAndReadPartyRolesAsync(cmd, query, cancellationToken);
        }
        catch (Exception e)
        {
            return new ThrowingAsyncEnumerable<PartyExternalRoleAssignmentRecord>(e).Using(cmd);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ExternalRoleAssignmentEvent> GetExternalRoleAssignmentStream(
        ulong fromExclusive,
        ushort limit,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const string QUERY =
            /*strpsql*/"""
            WITH maxval AS (
                SELECT register.tx_max_safeval('register.external_role_assignment_event_id_seq') maxval
            )
            SELECT
                "id",
                "type",
                "source",
                "identifier",
                "from_party",
                "to_party"
            FROM register.external_role_assignment_event
            CROSS JOIN maxval mv
            WHERE "id" > @from
              AND "id" <= mv.maxval
            ORDER BY "id"
            LIMIT @limit
            """;

        _handle.ThrowIfCompleted();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = QUERY;

        cmd.Parameters.Add<long>("from", NpgsqlDbType.Bigint).TypedValue = checked((long)fromExclusive);
        cmd.Parameters.Add<short>("limit", NpgsqlDbType.Smallint).TypedValue = checked((short)limit);

        await cmd.PrepareAsync(cancellationToken);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var idOrdinal = reader.GetOrdinal("id");
        var typeOrdinal = reader.GetOrdinal("type");
        var sourceOrdinal = reader.GetOrdinal("source");
        var identifierOrdinal = reader.GetOrdinal("identifier");
        var fromPartyOrdinal = reader.GetOrdinal("from_party");
        var toPartyOrdinal = reader.GetOrdinal("to_party");

        while (await reader.ReadAsync(cancellationToken))
        {
            var id = (ulong)await reader.GetFieldValueAsync<long>(idOrdinal, cancellationToken);
            var type = await reader.GetFieldValueAsync<ExternalRoleAssignmentEvent.EventType>(typeOrdinal, cancellationToken);
            var roleSource = await reader.GetFieldValueAsync<ExternalRoleSource>(sourceOrdinal, cancellationToken);
            var identifier = await reader.GetFieldValueAsync<string>(identifierOrdinal, cancellationToken);
            var toParty = await reader.GetFieldValueAsync<Guid>(toPartyOrdinal, cancellationToken);
            var fromParty = await reader.GetFieldValueAsync<Guid>(fromPartyOrdinal, cancellationToken);

            yield return new ExternalRoleAssignmentEvent
            {
                VersionId = id,
                Type = type,
                RoleSource = roleSource,
                RoleIdentifier = identifier,
                ToParty = toParty,
                FromParty = fromParty,
            };
        }
    }

    /// <inheritdoc/>
    public async Task<ulong> GetMaxExternalRoleAssignmentVersionId(CancellationToken cancellationToken)
    {
        const string QUERY =
            /*strpsql*/"""
            SELECT MAX(id) FROM register.external_role_assignment_event
            """;

        _handle.ThrowIfCompleted();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = QUERY;

        await cmd.PrepareAsync(cancellationToken);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            ThrowHelper.ThrowInvalidOperationException("No rows returned from MAX(id) query");
        }

        if (await reader.IsDBNullAsync(0, cancellationToken))
        {
            return 0;
        }

        return (ulong)await reader.GetFieldValueAsync<long>(0, cancellationToken);
    }

    private async IAsyncEnumerable<PartyExternalRoleAssignmentRecord> PrepareAndReadPartyRolesAsync(
        NpgsqlCommand inCmd,
        PartyRoleQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guard.IsNotNull(inCmd);
        Guard.IsNotNull(query);

        await using var cmd = inCmd;

        await cmd.PrepareAsync(cancellationToken);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var role = await query.ReadRole(reader, cancellationToken);
            yield return role;
        }
    }

    /// <inheritdoc/>
    public IAsyncSideEffectEnumerable<ExternalRoleAssignmentEvent> UpsertExternalRolesFromPartyBySource(
        Guid commandId,
        Guid partyUuid,
        ExternalRoleSource roleSource,
        PartyExternalRoleAssignmentsUpdate update,
        CancellationToken cancellationToken = default)
    {
        _handle.ThrowIfCompleted();
        Guard.IsNotNull(update);
        Guard.IsNotDefault(commandId);

        NpgsqlAsyncSideEffectEnumerable<ExternalRoleAssignmentEvent> enumerable;
        if (update.TryGetValue(out PartyExternalRoleAssignmentsUpdate.Full? full))
        {
            var assignmentList = full.Assignments.Select(static a => new ArgRoleAssignment
            {
                ToParty = ArgRolePartyRef.From(a.ToParty),
                Identifier = a.ExternalRoleIdentifier,
            }).ToList();

            Log.UpsertExternalRolesFromPartyBySource(_logger, assignmentList.Count, roleSource);
            enumerable = new UpsertExternalRolesFromPartyBySourceAsyncSideEffectEnumerable(
                connection: _connection,
                flags: _flags,
                now: _timeProvider.GetUtcNow(),
                commandId: commandId,
                partyUuid: partyUuid,
                roleSource: roleSource,
                assignments: assignmentList,
                cancellationToken: cancellationToken);
        }
        else if (update.TryGetValue(out PartyExternalRoleAssignmentsUpdate.Patch? delta))
        {
            var present = delta.Present.Select(static a => new ArgRoleAssignment
            {
                ToParty = ArgRolePartyRef.From(a.ToParty),
                Identifier = a.ExternalRoleIdentifier,
            }).ToList();

            var absent = delta.Absent.Select(static a => new ArgRoleAssignment
            {
                ToParty = ArgRolePartyRef.From(a.ToParty),
                Identifier = a.ExternalRoleIdentifier,
            }).ToList();

            var absentByIdentifier = delta.AbsentByIdentifier.ToList();

            Log.PatchExternalRolesFromPartyBySource(_logger, present.Count, absent.Count, absentByIdentifier.Count, roleSource);
            enumerable = new PatchExternalRolesFromPartyBySourcePatchAsyncSideEffectEnumerable(
                connection: _connection,
                flags: _flags,
                now: _timeProvider.GetUtcNow(),
                commandId: commandId,
                partyUuid: partyUuid,
                roleSource: roleSource,
                present: present,
                absent: absent,
                absentByIdentifier: absentByIdentifier,
                cancellationToken: cancellationToken);
        }
        else
        {
            throw new UnreachableException("Unknown update type");
        }

        return enumerable.WrapExceptions(ex => new UpsertExternalRolesFromPartyBySourceException(commandId, partyUuid, roleSource, update, ex), cancellationToken);
    }

    private sealed class UpsertExternalRolesFromPartyBySourceAsyncSideEffectEnumerable(
        NpgsqlConnection connection,
        PersistenceFeatureFlag[] flags,
        DateTimeOffset now,
        Guid commandId,
        Guid partyUuid,
        ExternalRoleSource roleSource,
        List<ArgRoleAssignment> assignments,
        CancellationToken cancellationToken = default)
        : NpgsqlAsyncSideEffectEnumerable<ExternalRoleAssignmentEvent>(connection, QUERY, cancellationToken)
    {
        private const string QUERY =
            /*strpsql*/"""
                SELECT
                    "version_id",
                    "type",
                    "identifier",
                    "to_party"
                FROM register.upsert_external_role_assignments(
                    @flags,
                    @now,
                    @from_party,
                    @source,
                    @cmd_id,
                    @assignments
                )
                """;

        /// <inheritdoc/>
        protected override void PrepareParameters(NpgsqlParameterCollection parameters)
        {
            parameters.Add<PersistenceFeatureFlag[]>("flags").TypedValue = flags;
            parameters.Add<DateTimeOffset>("now", NpgsqlDbType.TimestampTz).TypedValue = now;
            parameters.Add<Guid>("from_party", NpgsqlDbType.Uuid).TypedValue = partyUuid;
            parameters.Add<ExternalRoleSource>("source").TypedValue = roleSource;
            parameters.Add<Guid>("cmd_id", NpgsqlDbType.Uuid).TypedValue = commandId;
            var assignmentsParameter = parameters.Add<List<ArgRoleAssignment>>("assignments");
            assignmentsParameter.DataTypeName = "register.arg_role_assignment[]";
            assignmentsParameter.TypedValue = assignments;
        }

        /// <inheritdoc/>
        protected override async IAsyncEnumerator<ExternalRoleAssignmentEvent> Enumerate(
            NpgsqlDataReader reader,
            CancellationToken cancellationToken)
        {
            var versionIdOrdinal = reader.GetOrdinal("version_id");
            var typeOrdinal = reader.GetOrdinal("type");
            var identifierOrdinal = reader.GetOrdinal("identifier");
            var toPartyOrdinal = reader.GetOrdinal("to_party");

            while (await reader.ReadAsync(cancellationToken))
            {
                var versionId = (ulong)await reader.GetFieldValueAsync<long>(versionIdOrdinal, cancellationToken);
                var type = await reader.GetFieldValueAsync<ExternalRoleAssignmentEvent.EventType>(typeOrdinal, cancellationToken);
                var identifier = await reader.GetFieldValueAsync<string>(identifierOrdinal, cancellationToken);
                var toParty = await reader.GetFieldValueAsync<Guid>(toPartyOrdinal, cancellationToken);

                var evt = new ExternalRoleAssignmentEvent
                {
                    VersionId = versionId,
                    Type = type,
                    RoleSource = roleSource,
                    RoleIdentifier = identifier,
                    ToParty = toParty,
                    FromParty = partyUuid,
                };

                yield return evt;
            }
        }
    }

    private sealed class PatchExternalRolesFromPartyBySourcePatchAsyncSideEffectEnumerable(
        NpgsqlConnection connection,
        PersistenceFeatureFlag[] flags,
        DateTimeOffset now,
        Guid commandId,
        Guid partyUuid,
        ExternalRoleSource roleSource,
        List<ArgRoleAssignment> present,
        List<ArgRoleAssignment> absent,
        List<string> absentByIdentifier,
        CancellationToken cancellationToken = default)
        : NpgsqlAsyncSideEffectEnumerable<ExternalRoleAssignmentEvent>(connection, QUERY, cancellationToken)
    {
        private const string QUERY =
            /*strpsql*/"""
                SELECT
                    "version_id",
                    "type",
                    "identifier",
                    "to_party"
                FROM register.patch_external_role_assignments(
                    @flags,
                    @now,
                    @from_party,
                    @source,
                    @cmd_id,
                    @present,
                    @absent,
                    @absent_by_identifier
                )
                """;

        /// <inheritdoc/>
        protected override void PrepareParameters(NpgsqlParameterCollection parameters)
        {
            parameters.Add<PersistenceFeatureFlag[]>("flags").TypedValue = flags;
            parameters.Add<DateTimeOffset>("now", NpgsqlDbType.TimestampTz).TypedValue = now;
            parameters.Add<Guid>("from_party", NpgsqlDbType.Uuid).TypedValue = partyUuid;
            parameters.Add<ExternalRoleSource>("source").TypedValue = roleSource;
            parameters.Add<Guid>("cmd_id", NpgsqlDbType.Uuid).TypedValue = commandId;

            var presentParameter = parameters.Add<List<ArgRoleAssignment>>("present");
            presentParameter.DataTypeName = "register.arg_role_assignment[]";
            presentParameter.TypedValue = present;

            var absentParameter = parameters.Add<List<ArgRoleAssignment>>("absent");
            absentParameter.DataTypeName = "register.arg_role_assignment[]";
            absentParameter.TypedValue = absent;

            var absentByIdentifierParameter = parameters.Add<List<string>>("absent_by_identifier");
            absentByIdentifierParameter.DataTypeName = "text[]";
            absentByIdentifierParameter.TypedValue = absentByIdentifier;
        }

        /// <inheritdoc/>
        protected override async IAsyncEnumerator<ExternalRoleAssignmentEvent> Enumerate(
            NpgsqlDataReader reader,
            CancellationToken cancellationToken)
        {
            var versionIdOrdinal = reader.GetOrdinal("version_id");
            var typeOrdinal = reader.GetOrdinal("type");
            var identifierOrdinal = reader.GetOrdinal("identifier");
            var toPartyOrdinal = reader.GetOrdinal("to_party");

            while (await reader.ReadAsync(cancellationToken))
            {
                var versionId = (ulong)await reader.GetFieldValueAsync<long>(versionIdOrdinal, cancellationToken);
                var type = await reader.GetFieldValueAsync<ExternalRoleAssignmentEvent.EventType>(typeOrdinal, cancellationToken);
                var identifier = await reader.GetFieldValueAsync<string>(identifierOrdinal, cancellationToken);
                var toParty = await reader.GetFieldValueAsync<Guid>(toPartyOrdinal, cancellationToken);

                var evt = new ExternalRoleAssignmentEvent
                {
                    VersionId = versionId,
                    Type = type,
                    RoleSource = roleSource,
                    RoleIdentifier = identifier,
                    ToParty = toParty,
                    FromParty = partyUuid,
                };

                yield return evt;
            }
        }
    }

    [Flags]
    private enum PartyRoleFilters
        : byte
    {
        None = 0,
        FromParty = 1 << 0,
        ToParty = 1 << 1,

        RoleSource = 1 << 2,
        Role = RoleSource | (1 << 3),
    }

    #endregion

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Debug, "Upserting {Count} external role-assignments from {Source}")]
        public static partial void UpsertExternalRolesFromPartyBySource(ILogger logger, int count, ExternalRoleSource source);

        [LoggerMessage(1, LogLevel.Debug, "Patching external role-assignments from {Source} with {PresentCount} present, {AbsentCount} absent and {AbsentByIdentifierCount} absent-by-identifier")]
        public static partial void PatchExternalRolesFromPartyBySource(ILogger logger, int presentCount, int absentCount, int absentByIdentifierCount, ExternalRoleSource source);
    }

    private sealed class UpsertExternalRolesFromPartyBySourceException
        : InvalidOperationException
    {
        public Guid CommandId { get; }

        public Guid FromParty { get; }

        public ExternalRoleSource RoleSource { get; }

        // Note: We're explicitly not keeping the inner exception here, because the resulting exception is too long and causes issues in logging
        public UpsertExternalRolesFromPartyBySourceException(
            Guid commandId,
            Guid fromParty,
            ExternalRoleSource source,
            PartyExternalRoleAssignmentsUpdate update,
            Exception innerException)
            : base(CreateMessage(commandId, fromParty, source, update, innerException))
        {
            CommandId = commandId;
            FromParty = fromParty;
            RoleSource = source;
        }

        private static string CreateMessage(
            Guid commandId,
            Guid fromParty,
            ExternalRoleSource source,
            PartyExternalRoleAssignmentsUpdate update,
            Exception innerException)
        {
            if (update.TryGetValue(out PartyExternalRoleAssignmentsUpdate.Full? full))
            {
                return CreateMessage(commandId, fromParty, source, full, innerException);
            }
            else if (update.TryGetValue(out PartyExternalRoleAssignmentsUpdate.Patch? delta))
            {
                return CreateMessage(commandId, fromParty, source, delta, innerException);
            }
            else
            {
                throw new UnreachableException("Unknown update type");
            }
        }

        private static string CreateMessage(
            Guid commandId,
            Guid fromParty,
            ExternalRoleSource source,
            PartyExternalRoleAssignmentsUpdate.Full full,
            Exception innerException)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Failed to upsert external role-assignments from party '{fromParty}' for source '{source}';");
            sb.AppendLine($"Cause By: {innerException.Message}");
            sb.AppendLine($"CommandId: {{{commandId}}}");

            foreach (var assignment in full.Assignments)
            {
                sb.AppendLine($"  {assignment.ExternalRoleIdentifier} -> {LogSafe(assignment.ToParty)}");
            }

            return sb.ToString();
        }

        private static string CreateMessage(
            Guid commandId,
            Guid fromParty,
            ExternalRoleSource source,
            PartyExternalRoleAssignmentsUpdate.Patch delta,
            Exception innerException)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Failed to update external role-assignments from party '{fromParty}' for source '{source}';");
            sb.AppendLine($"Cause By: {innerException.Message}");
            sb.AppendLine($"CommandId: {{{commandId}}}");

            foreach (var roleIdentifier in delta.AbsentByIdentifier)
            {
                sb.AppendLine($"  REMOVE ALL: {roleIdentifier}");
            }

            foreach (var rem in delta.Absent)
            {
                sb.AppendLine($"  REMOVE: {rem.ExternalRoleIdentifier} -> {LogSafe(rem.ToParty)}");
            }

            foreach (var add in delta.Present)
            {
                sb.AppendLine($"  ADD: {add.ExternalRoleIdentifier} -> {LogSafe(add.ToParty)}");
            }

            return sb.ToString();
        }

        private static string LogSafe(PartyExternalRoleAssignmentPartyRef partyRef)
        {
            return partyRef switch
            {
                PartyExternalRoleAssignmentPartyRef.PartyUuid p => p.Uuid.ToString(),
                PartyExternalRoleAssignmentPartyRef.Organization o => o.OrganizationIdentifier.ToString(),
                PartyExternalRoleAssignmentPartyRef.Person => "REDACTED_PERSON_ID",
                _ => "UNKNOWN_PARTY_REF",
            };
        }
    }
}
