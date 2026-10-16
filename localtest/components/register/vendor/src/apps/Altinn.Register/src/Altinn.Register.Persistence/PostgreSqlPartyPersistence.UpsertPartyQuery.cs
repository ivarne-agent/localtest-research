using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ServiceDefaults.Npgsql;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Urn;
using CommunityToolkit.Diagnostics;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Register.Persistence;

/// <content>
/// Contains the upsert party functionality.
/// </content>
internal partial class PostgreSqlPartyPersistence
{
    /// <summary>
    /// Upsert party query helper.
    /// </summary>
    internal abstract class UpsertPartyQuery
    {
        private const int MAX_BATCH_SIZE = 1_000;

        private static readonly UpsertPersonQuery _person = new();
        private static readonly UpsertOrganizationParty _org = new();
        private static readonly UpsertSelfIdentifiedUserQuery _si = new();
        private static readonly UpsertSystemUserQuery _su = new();
        private static readonly UpsertEnterpriseUserQuery _eu = new();

        /// <summary>
        /// Upserts a party.
        /// </summary>
        /// <param name="conn">The connection.</param>
        /// <param name="party">The party.</param>
        /// <param name="flags">The persistence feature-flags to use for this operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>The updated party.</returns>
        public static ValueTask<Result<PartyRecord>> UpsertParty(
            NpgsqlConnection conn,
            PartyRecord party,
            PersistenceFeatureFlag[] flags,
            CancellationToken cancellationToken)
            => UpsertParties(conn, new AsyncSingleton(party), flags, cancellationToken)
                .FirstAsync(cancellationToken);

        /// <summary>
        /// Upserts multiple parties.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <param name="parties">The parties.</param>
        /// <param name="flags">The persistence feature-flags to use for this operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>The updated parties.</returns>
        public static async IAsyncEnumerable<Result<PartyRecord>> UpsertParties(
            NpgsqlConnection connection,
            IAsyncEnumerable<PartyRecord> parties,
            PersistenceFeatureFlag[] flags,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var batch = connection.CreateBatch();

            try
            {
                await foreach (var party in parties.WithCancellation(cancellationToken))
                {
                    switch (party)
                    {
                        case PersonRecord person:
                            _person.EnqueuePartyUpsert(batch, person, flags);
                            break;

                        case OrganizationRecord org:
                            _org.EnqueuePartyUpsert(batch, org, flags);
                            break;

                        case SelfIdentifiedUserRecord siu:
                            _si.EnqueuePartyUpsert(batch, siu, flags);
                            break;

                        case SystemUserRecord su:
                            _su.EnqueuePartyUpsert(batch, su, flags);
                            break;

                        case EnterpriseUserRecord eu:
                            _eu.EnqueuePartyUpsert(batch, eu, flags);
                            break;

                        default:
                            ThrowHelper.ThrowArgumentException<Result<PartyRecord>>("Unsupported party type for batch upsert", nameof(party));
                            break;
                    }

                    if (batch.BatchCommands.Count >= MAX_BATCH_SIZE)
                    {
                        await foreach (var result in ExecuteBatch(batch, cancellationToken))
                        {
                            yield return result;

                            if (result.IsProblem)
                            {
                                yield break;
                            }
                        }

                        await batch.DisposeAsync();
                        batch = connection.CreateBatch();
                    }
                }

                if (batch.BatchCommands.Count > 0)
                {
                    await foreach (var result in ExecuteBatch(batch, cancellationToken))
                    {
                        yield return result;

                        if (result.IsProblem)
                        {
                            yield break;
                        }
                    }
                }
            }
            finally
            {
                await batch.DisposeAsync();
            }

            static async IAsyncEnumerable<Result<PartyRecord>> ExecuteBatch(
                NpgsqlBatch batch,
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await using var batchEnumerator = ExecuteBatchInner(batch, cancellationToken).GetAsyncEnumerator(cancellationToken);

                bool hasMore = false;
                ProblemInstance? problem = null;

                while (true)
                {
                    try
                    {
                        hasMore = await batchEnumerator.MoveNextAsync();
                    }
                    catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
                    {
                        problem = Problems.PartyConflict.Create([
                            new("constraint", e.ConstraintName ?? "<unknown>"),
                        ]);
                    }
                    catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.NotNullViolation && e.ColumnName == "display_name")
                    {
                        // This happens if the upsert was intended to be a partial update (having no display_name set)
                        // but the party did not exist beforehand, so the upsert attempted to create a new party without
                        // a display name, which is not allowed.
                        problem = Problems.PartyNotFound;
                    }
                    catch (PostgresException e) when (e.SqlState == "ZZ001")
                    {
                        // ZZ001 is a custom SQLSTATE code used to indicate that the party update is invalid
                        problem = Problems.InvalidPartyUpdate.Create([
                            new("message", e.MessageText),
                            new("column", e.ColumnName ?? "<unknown>"),
                        ]);
                    }

                    if (problem is not null)
                    {
                        yield return problem;
                        break;
                    }

                    if (!hasMore)
                    {
                        break;
                    }

                    yield return batchEnumerator.Current;
                }
            }

            static async IAsyncEnumerable<Result<PartyRecord>> ExecuteBatchInner(
                NpgsqlBatch batch,
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await batch.PrepareAsync(cancellationToken);
                await using var reader = await batch.ExecuteReaderAsync(cancellationToken);

                do
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var partyType = await reader.GetConditionalFieldValueAsync<PartyRecordType>("p_party_type", cancellationToken);
                        Debug.Assert(partyType.HasValue);

                        Result<PartyRecord> result = partyType.Value switch
                        {
                            PartyRecordType.Person => await _person.ReadResult(reader, cancellationToken),
                            PartyRecordType.Organization => await _org.ReadResult(reader, cancellationToken),
                            PartyRecordType.SelfIdentifiedUser => await _si.ReadResult(reader, cancellationToken),
                            PartyRecordType.EnterpriseUser => await _eu.ReadResult(reader, cancellationToken),
                            PartyRecordType.SystemUser => await _su.ReadResult(reader, cancellationToken),
                            _ => ThrowHelper.ThrowInvalidOperationException<Result<PartyRecord>>("Unsupported party type from batch upsert"),
                        };

                        yield return result;
                    }
                }
                while (await reader.NextResultAsync(cancellationToken));
            }
        }

        /// <summary>
        /// Gets or creates a self-identified email user based on email.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <param name="email">The email of the self-identified user.</param>
        /// <param name="now">The current date and time.</param>
        /// <param name="flags">The persistence feature-flags to use for this operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>The result of the operation.</returns>
        public static async Task<Result<NewOrExisting<SelfIdentifiedUserRecord>>> GetOrCreateSelfIdentifiedEmailUser(
            NpgsqlConnection connection,
            string email,
            DateTimeOffset now,
            PersistenceFeatureFlag[] flags,
            CancellationToken cancellationToken)
        {
            const string QUERY =
                /*strpsql*/"""
                SELECT *
                FROM register.get_or_create_email_user(
                    @flags,
                    @extUrn,
                    @now, -- created at
                    @now, -- updated at
                    @email)
                """;

            Debug.Assert(string.Equals(email.ToLowerInvariant(), email, StringComparison.Ordinal));
            await using var cmd = connection.CreateCommand(QUERY);

            cmd.Parameters.Add<PersistenceFeatureFlag[]>("flags").TypedValue = flags;
            cmd.Parameters.Add<string>("extUrn", NpgsqlDbType.Text).TypedValue = PartyExternalRefUrn.IDPortenEmail.Create(UrnEncoded.Create(email)).ToString();
            cmd.Parameters.Add<DateTimeOffset>("now", NpgsqlDbType.TimestampTz).TypedValue = now;
            cmd.Parameters.Add<string>("email", NpgsqlDbType.Text).TypedValue = email;

            await cmd.PrepareAsync(cancellationToken);
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow | CommandBehavior.SingleResult, cancellationToken);

            var read = await reader.ReadAsync(cancellationToken);
            Debug.Assert(read, "Expected a row from get_or_create_email_user");

            var isNew = await reader.GetFieldValueAsync<bool>("o_is_new", cancellationToken);
            var siUser = await _si.ReadResult(reader, cancellationToken);

            return isNew
                ? NewOrExisting.New(siUser)
                : NewOrExisting.Existing(siUser);
        }

        /// <summary>
        /// Gets or creates a self-identified educational user based on an external reference.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <param name="extRef">The external reference of the self-identified educational user.</param>
        /// <param name="username">The username of the self-identified educational user. This is only used if the user is created.</param>
        /// <param name="now">The current date and time.</param>
        /// <param name="flags">The persistence feature-flags to use for this operation.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>The result of the operation.</returns>
        public static async Task<Result<NewOrExisting<SelfIdentifiedUserRecord>>> GetOrCreateSelfIdentifiedEduUser(
            NpgsqlConnection connection,
            string extRef,
            string username,
            DateTimeOffset now,
            PersistenceFeatureFlag[] flags,
            CancellationToken cancellationToken)
        {
            const string QUERY =
                /*strpsql*/"""
                SELECT *
                FROM register.get_or_create_edu_user(
                    @flags,
                    @displayName,
                    @now, -- created at
                    @now, -- updated at
                    @extRef)
                """;

            await using var cmd = connection.CreateCommand(QUERY);

            cmd.Parameters.Add<PersistenceFeatureFlag[]>("flags").TypedValue = flags;
            cmd.Parameters.Add<string>("displayName", NpgsqlDbType.Text).TypedValue = username;
            cmd.Parameters.Add<DateTimeOffset>("now", NpgsqlDbType.TimestampTz).TypedValue = now;
            cmd.Parameters.Add<string>("extRef", NpgsqlDbType.Text).TypedValue = extRef;

            await cmd.PrepareAsync(cancellationToken);
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow | CommandBehavior.SingleResult, cancellationToken);

            var read = await reader.ReadAsync(cancellationToken);
            Debug.Assert(read, "Expected a row from get_or_create_edu_user");

            var isNew = await reader.GetFieldValueAsync<bool>("o_is_new", cancellationToken);
            var siUser = await _si.ReadResult(reader, cancellationToken);

            return isNew
                ? NewOrExisting.New(siUser)
                : NewOrExisting.Existing(siUser);
        }

        private const string DEFAULT_QUERY =
            /*strpsql*/"""
            SELECT *
            FROM register.upsert_party(
                @flags,
                @set_uuid, @uuid,
                @set_id, @id,
                @ext_urn,
                @user_ids,
                @set_username, @username,
                @party_type,
                @set_display_name, @display_name,
                @person_id,
                @org_id,
                @created_at,
                @modified_at,
                @set_is_deleted, @is_deleted,
                @set_deleted_at, @deleted_at,
                @set_owner, @owner)
            """;

        /// <summary>
        /// Attempts to find the zero-based column ordinal for the specified column name in the provided data reader.
        /// </summary>
        /// <remarks>
        /// If <a href="https://github.com/npgsql/npgsql/issues/6423">Npgsql #6423</a> ever get's implemented, this can be removed.
        /// </remarks>
        /// <param name="reader">The data reader to search for the column.</param>
        /// <param name="columnName">The name of the column to locate. The comparison is case-insensitive.</param>
        /// <param name="ordinal">When this method returns, contains the zero-based ordinal of the column if found; otherwise, -1. This
        /// parameter is passed uninitialized.</param>
        /// <returns><see langword="true"/> if the column is found; otherwise, <see langword="false"/>.</returns>
        protected static bool TryGetOrdinal(NpgsqlDataReader reader, string columnName, out int ordinal)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    ordinal = i;
                    return true;
                }
            }

            ordinal = -1;
            return false;
        }

        /// <summary>
        /// Reads user information from a data reader.
        /// </summary>
        protected static async Task<FieldValue<PartyHistoricalAggregate<uint>>> ReadUserIds(NpgsqlDataReader reader, CancellationToken cancellationToken)
        {
            var fromDbUserIds = await reader.GetConditionalFieldValueAsync<int[]>("p_user_ids", cancellationToken);
            //// var username = await reader.GetConditionalFieldValueAsync<string>("p_username", cancellationToken);

            if (!fromDbUserIds.HasValue)
            {
                return FieldValue.Unset;
            }

            var userIds = fromDbUserIds.Value.Select(static id => checked((uint)id)).ToImmutableValueArray();
            return PartyHistoricalAggregate<uint>.Create(userIds, hasActiveValue: true);
        }

        /// <summary>
        /// Reads usernames from a data reader.
        /// </summary>
        protected static async Task<FieldValue<PartyHistoricalAggregate<string>>> ReadUsernames(NpgsqlDataReader reader, CancellationToken cancellationToken)
        {
            var username = await reader.GetConditionalFieldValueAsync<string>("p_username", cancellationToken);

            return username switch
            {
                { IsUnset: true } => FieldValue.Unset,
                { IsNull: true } => PartyHistoricalAggregate<string>.Empty,
                { Value: var name } => PartyHistoricalAggregate<string>.CreateCurrent(name!),
            };
        }

        private abstract class Typed<T>(PartyRecordType type)
            : UpsertPartyQuery
            where T : PartyRecord
        {
            protected virtual string GetQuery(T party)
                => DEFAULT_QUERY;

            protected virtual void ValidateFields(T party, PersistenceFeatureFlag[] flags)
            {
                Debug.Assert(flags.Contains(PersistenceFeatureFlag.CreatePartyId) || party.PartyUuid.HasValue);
                Debug.Assert(flags.Contains(PersistenceFeatureFlag.CreatePartyId) || party.ExternalUrn.IsSet);
                Debug.Assert(party.UserIds.IsUnset || (party.UserIds.HasValue && (party.UserIds.Value.HasCurrentValue || party.UserIds.Value.IsEmpty)));
                Debug.Assert(party.Usernames.IsUnset || (party.Usernames.HasValue && !party.Usernames.Value.HasHistoricalValues));
                Debug.Assert(party.PartyType.HasValue && party.PartyType.Value == type);
                Debug.Assert(flags.Contains(PersistenceFeatureFlag.CreatePartyId) || party.DisplayName.HasValue);
                Debug.Assert(party.PersonIdentifier.IsSet);
                Debug.Assert(party.OrganizationIdentifier.IsSet);
                Debug.Assert(party.CreatedAt.HasValue);
                Debug.Assert(party.ModifiedAt.HasValue);
                Debug.Assert(!party.IsDeleted.IsNull);
                Debug.Assert(party.DeletedAt.IsUnset || (party.IsDeleted.HasValue && party.IsDeleted.Value == party.DeletedAt.HasValue));

                if (type is PartyRecordType.Person or PartyRecordType.Organization or PartyRecordType.SelfIdentifiedUser)
                {
                    Debug.Assert(flags.Contains(PersistenceFeatureFlag.CreatePartyId) || party.PartyId.HasValue);
                }
                else
                {
                    Debug.Assert(party.PartyId.IsNull);
                }
            }

            protected virtual void AddPartyParameters(NpgsqlParameterCollection parameters, T party, PersistenceFeatureFlag[] flags)
            {
                int[]? userIds = party.UserIds.Values switch
                {
                    { IsUnset: true } or { IsNull: true } or { Value.IsEmpty: true } => null,
                    { Value: var ids } => [.. ids.Select(static id => checked((int)id))],
                };

                var username = party.Usernames.CurrentValue;

                parameters.Add<PersistenceFeatureFlag[]>("flags").TypedValue = flags;
                parameters.AddOptional("set_uuid", "uuid", NpgsqlDbType.Uuid, party.PartyUuid);
                parameters.AddOptional("set_id", "id", NpgsqlDbType.Bigint, party.PartyId.Select(static id => checked((long)id)));
                parameters.Add<string?>("ext_urn", NpgsqlDbType.Text).TypedValue = party.ExternalUrn.Value?.Urn;
                parameters.Add<int[]?>("user_ids", NpgsqlDbType.Bigint | NpgsqlDbType.Array).TypedValue = userIds;
                parameters.AddOptional("set_username", "username", NpgsqlDbType.Text, username);
                parameters.Add<PartyRecordType>("party_type").TypedValue = party.PartyType.Value;
                parameters.AddOptional("set_display_name", "display_name", NpgsqlDbType.Text, party.DisplayName);
                parameters.Add<string>("person_id", NpgsqlDbType.Text).TypedValue = party.PersonIdentifier.IsNull ? null : party.PersonIdentifier.Value!.ToString();
                parameters.Add<string>("org_id", NpgsqlDbType.Text).TypedValue = party.OrganizationIdentifier.IsNull ? null : party.OrganizationIdentifier.Value!.ToString();
                parameters.Add<DateTimeOffset>("created_at", NpgsqlDbType.TimestampTz).TypedValue = party.CreatedAt.Value.ToUniversalTime();
                parameters.Add<DateTimeOffset>("modified_at", NpgsqlDbType.TimestampTz).TypedValue = party.ModifiedAt.Value.ToUniversalTime();
                parameters.AddOptional("set_is_deleted", "is_deleted", NpgsqlDbType.Boolean, party.IsDeleted);
                parameters.AddOptional("set_deleted_at", "deleted_at", NpgsqlDbType.TimestampTz, party.DeletedAt.Select(static v => v.ToUniversalTime()));
                parameters.AddOptional("set_owner", "owner", NpgsqlDbType.Uuid, party.OwnerUuid);
            }

            public abstract Task<T> ReadResult(NpgsqlDataReader reader, CancellationToken cancellationToken);

            public void EnqueuePartyUpsert(NpgsqlBatch batch, T party, PersistenceFeatureFlag[] flags)
            {
                ValidateFields(party, flags);

                var cmd = batch.CreateBatchCommand(GetQuery(party));
                AddPartyParameters(cmd.Parameters, party, flags);
            }
        }

        private sealed class UpsertPersonQuery()
            : Typed<PersonRecord>(PartyRecordType.Person)
        {
            private const string QUERY =
                /*strpsql*/"""
                SELECT *
                FROM register.upsert_party_pers(
                    @flags,
                    @set_uuid, @uuid,
                    @set_id, @id,
                    @ext_urn,
                    @user_ids,
                    @set_username, @username,
                    @party_type,
                    @set_display_name, @display_name,
                    @person_id,
                    @org_id,
                    @created_at,
                    @modified_at,
                    @set_is_deleted, @is_deleted,
                    @set_deleted_at, @deleted_at,
                    @set_owner, @owner,
                    @set_first_name, @first_name,
                    @set_middle_name, @middle_name,
                    @set_last_name, @last_name,
                    @set_short_name, @short_name,
                    @set_date_of_birth, @date_of_birth,
                    @set_date_of_death, @date_of_death,
                    @set_address, @address,
                    @set_mailing_address, @mailing_address,
                    @set_source, @source)
                """;

            protected override string GetQuery(PersonRecord party)
                => QUERY;

            protected override void ValidateFields(PersonRecord party, PersistenceFeatureFlag[] flags)
            {
                base.ValidateFields(party, flags);
                Debug.Assert(!party.Source.IsNull, "person cannot have source = null");
                Debug.Assert(!party.FirstName.IsNull, "person must have FirstName != null");
                Debug.Assert(!party.LastName.IsNull, "person must have LastName != null");
                Debug.Assert(!party.ShortName.IsNull, "person must have ShortName != null");
                Debug.Assert(!party.OwnerUuid.HasValue, "person cannot have OwnerUuid set");
            }

            protected override void AddPartyParameters(NpgsqlParameterCollection parameters, PersonRecord party, PersistenceFeatureFlag[] flags)
            {
                base.AddPartyParameters(parameters, party, flags);
                parameters.AddOptional("set_first_name", "first_name", NpgsqlDbType.Text, party.FirstName);
                parameters.AddOptional("set_middle_name", "middle_name", NpgsqlDbType.Text, party.MiddleName);
                parameters.AddOptional("set_last_name", "last_name", NpgsqlDbType.Text, party.LastName);
                parameters.AddOptional("set_short_name", "short_name", NpgsqlDbType.Text, party.ShortName);
                parameters.AddOptional("set_date_of_birth", "date_of_birth", party.DateOfBirth);
                parameters.AddOptional("set_date_of_death", "date_of_death", party.DateOfDeath);
                parameters.AddOptional("set_address", "address", party.Address);
                parameters.AddOptional("set_mailing_address", "mailing_address", party.MailingAddress);
                parameters.AddOptional("set_source", "source", party.Source);
            }

            public override async Task<PersonRecord> ReadResult(NpgsqlDataReader reader, CancellationToken cancellationToken)
            {
                return new PersonRecord
                {
                    PartyUuid = await reader.GetConditionalFieldValueAsync<Guid>("p_uuid", cancellationToken),
                    OwnerUuid = await reader.GetConditionalFieldValueAsync<Guid>("p_owner", cancellationToken),
                    PartyId = await reader.GetConditionalFieldValueAsync<int>("p_id", cancellationToken).Select(static id => checked((uint)id)),
                    ExternalUrn = await reader.GetConditionalParsableFieldValueAsync<PartyExternalRefUrn>("p_ext_urn", cancellationToken),
                    UserIds = await ReadUserIds(reader, cancellationToken),
                    Usernames = await ReadUsernames(reader, cancellationToken),
                    DisplayName = await reader.GetConditionalFieldValueAsync<string>("p_display_name", cancellationToken),
                    PersonIdentifier = await reader.GetConditionalParsableFieldValueAsync<PersonIdentifier>("p_person_identifier", cancellationToken),
                    OrganizationIdentifier = await reader.GetConditionalParsableFieldValueAsync<OrganizationIdentifier>("p_organization_identifier", cancellationToken),
                    CreatedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>("p_created", cancellationToken),
                    ModifiedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>("p_updated", cancellationToken),
                    IsDeleted = await reader.GetConditionalFieldValueAsync<bool>("p_is_deleted", cancellationToken),
                    DeletedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>("p_deleted_at", cancellationToken),
                    VersionId = await reader.GetConditionalFieldValueAsync<long>("o_version_id", cancellationToken).Select(static v => (ulong)v),

                    FirstName = await reader.GetConditionalFieldValueAsync<string>("p_first_name", cancellationToken),
                    MiddleName = await reader.GetConditionalFieldValueAsync<string>("p_middle_name", cancellationToken),
                    LastName = await reader.GetConditionalFieldValueAsync<string>("p_last_name", cancellationToken),
                    ShortName = await reader.GetConditionalFieldValueAsync<string>("p_short_name", cancellationToken),
                    DateOfBirth = await reader.GetConditionalFieldValueAsync<DateOnly>("p_date_of_birth", cancellationToken),
                    DateOfDeath = await reader.GetConditionalFieldValueAsync<DateOnly>("p_date_of_death", cancellationToken),
                    Address = await reader.GetConditionalFieldValueAsync<StreetAddressRecord>("p_address", cancellationToken),
                    MailingAddress = await reader.GetConditionalFieldValueAsync<MailingAddressRecord>("p_mailing_address", cancellationToken),
                    Source = await reader.GetConditionalFieldValueAsync<PersonSource>("p_source", cancellationToken),
                };
            }
        }

        private sealed class UpsertOrganizationParty()
            : Typed<OrganizationRecord>(PartyRecordType.Organization)
        {
            private const string QUERY =
                /*strpsql*/"""
                SELECT *
                FROM register.upsert_party_org(
                    @flags,
                    @set_uuid, @uuid,
                    @set_id, @id,
                    @ext_urn,
                    @user_ids,
                    @set_username, @username,
                    @party_type,
                    @set_display_name, @display_name,
                    @person_id,
                    @org_id,
                    @created_at,
                    @modified_at,
                    @set_is_deleted, @is_deleted,
                    @set_deleted_at, @deleted_at,
                    @set_owner, @owner,
                    @set_unit_status, @unit_status,
                    @set_unit_type, @unit_type,
                    @set_telephone_number, @telephone_number,
                    @set_mobile_number, @mobile_number,
                    @set_fax_number, @fax_number,
                    @set_email_address, @email_address,
                    @set_internet_address, @internet_address,
                    @set_mailing_address, @mailing_address,
                    @set_business_address, @business_address,
                    @set_source, @source)
                """;

            protected override string GetQuery(OrganizationRecord party)
                => QUERY;

            protected override void ValidateFields(OrganizationRecord party, PersistenceFeatureFlag[] flags)
            {
                base.ValidateFields(party, flags);
                Debug.Assert(party.UnitStatus.HasValue);
                Debug.Assert(party.UnitType.HasValue);
                Debug.Assert(flags.Contains(PersistenceFeatureFlag.CreatePartyId) || party.TelephoneNumber.IsSet);
                Debug.Assert(flags.Contains(PersistenceFeatureFlag.CreatePartyId) || party.MobileNumber.IsSet);
                Debug.Assert(flags.Contains(PersistenceFeatureFlag.CreatePartyId) || party.FaxNumber.IsSet);
                Debug.Assert(flags.Contains(PersistenceFeatureFlag.CreatePartyId) || party.EmailAddress.IsSet);
                Debug.Assert(flags.Contains(PersistenceFeatureFlag.CreatePartyId) || party.InternetAddress.IsSet);
                Debug.Assert(flags.Contains(PersistenceFeatureFlag.CreatePartyId) || party.MailingAddress.IsSet);
                Debug.Assert(flags.Contains(PersistenceFeatureFlag.CreatePartyId) || party.BusinessAddress.IsSet);
                Debug.Assert(!party.OwnerUuid.HasValue, "organization cannot have OwnerUuid set");
            }

            protected override void AddPartyParameters(NpgsqlParameterCollection parameters, OrganizationRecord party, PersistenceFeatureFlag[] flags)
            {
                base.AddPartyParameters(parameters, party, flags);
                parameters.AddOptional("set_unit_status", "unit_status", NpgsqlDbType.Text, party.UnitStatus);
                parameters.AddOptional("set_unit_type", "unit_type", NpgsqlDbType.Text, party.UnitType);
                parameters.AddOptional("set_telephone_number", "telephone_number", NpgsqlDbType.Text, party.TelephoneNumber);
                parameters.AddOptional("set_mobile_number", "mobile_number", NpgsqlDbType.Text, party.MobileNumber);
                parameters.AddOptional("set_fax_number", "fax_number", NpgsqlDbType.Text, party.FaxNumber);
                parameters.AddOptional("set_email_address", "email_address", NpgsqlDbType.Text, party.EmailAddress);
                parameters.AddOptional("set_internet_address", "internet_address", NpgsqlDbType.Text, party.InternetAddress);
                parameters.AddOptional("set_mailing_address", "mailing_address", party.MailingAddress);
                parameters.AddOptional("set_business_address", "business_address", party.BusinessAddress);
                parameters.AddOptional("set_source", "source", party.Source);
            }

            public override async Task<OrganizationRecord> ReadResult(NpgsqlDataReader reader, CancellationToken cancellationToken)
            {
                return new OrganizationRecord
                {
                    PartyUuid = await reader.GetConditionalFieldValueAsync<Guid>("p_uuid", cancellationToken),
                    OwnerUuid = await reader.GetConditionalFieldValueAsync<Guid>("p_owner", cancellationToken),
                    PartyId = await reader.GetConditionalFieldValueAsync<int>("p_id", cancellationToken).Select(static id => checked((uint)id)),
                    ExternalUrn = await reader.GetConditionalParsableFieldValueAsync<PartyExternalRefUrn>("p_ext_urn", cancellationToken),
                    UserIds = await ReadUserIds(reader, cancellationToken),
                    Usernames = await ReadUsernames(reader, cancellationToken),
                    DisplayName = await reader.GetConditionalFieldValueAsync<string>("p_display_name", cancellationToken),
                    PersonIdentifier = await reader.GetConditionalParsableFieldValueAsync<PersonIdentifier>("p_person_identifier", cancellationToken),
                    OrganizationIdentifier = await reader.GetConditionalParsableFieldValueAsync<OrganizationIdentifier>("p_organization_identifier", cancellationToken),
                    CreatedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>("p_created", cancellationToken),
                    ModifiedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>("p_updated", cancellationToken),
                    IsDeleted = await reader.GetConditionalFieldValueAsync<bool>("p_is_deleted", cancellationToken),
                    DeletedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>("p_deleted_at", cancellationToken),
                    VersionId = await reader.GetConditionalFieldValueAsync<long>("o_version_id", cancellationToken).Select(static v => (ulong)v),

                    UnitStatus = await reader.GetConditionalFieldValueAsync<string>("p_unit_status", cancellationToken),
                    UnitType = await reader.GetConditionalFieldValueAsync<string>("p_unit_type", cancellationToken),
                    TelephoneNumber = await reader.GetConditionalFieldValueAsync<string>("p_telephone_number", cancellationToken),
                    MobileNumber = await reader.GetConditionalFieldValueAsync<string>("p_mobile_number", cancellationToken),
                    FaxNumber = await reader.GetConditionalFieldValueAsync<string>("p_fax_number", cancellationToken),
                    EmailAddress = await reader.GetConditionalFieldValueAsync<string>("p_email_address", cancellationToken),
                    InternetAddress = await reader.GetConditionalFieldValueAsync<string>("p_internet_address", cancellationToken),
                    MailingAddress = await reader.GetConditionalFieldValueAsync<MailingAddressRecord>("p_mailing_address", cancellationToken),
                    BusinessAddress = await reader.GetConditionalFieldValueAsync<MailingAddressRecord>("p_business_address", cancellationToken),
                    Source = await reader.GetConditionalFieldValueAsync<OrganizationSource>("p_source", cancellationToken),

                    ParentOrganizationUuid = FieldValue.Unset,
                };
            }
        }

        private sealed class UpsertSelfIdentifiedUserQuery()
            : Typed<SelfIdentifiedUserRecord>(PartyRecordType.SelfIdentifiedUser)
        {
            private const string QUERY =
                /*strpsql*/"""
                SELECT *
                FROM register.upsert_self_identified_user(
                    @flags,
                    @set_uuid, @uuid,
                    @set_id, @id,
                    @ext_urn,
                    @user_ids,
                    @set_username, @username,
                    @party_type,
                    @set_display_name, @display_name,
                    @person_id,
                    @org_id,
                    @created_at,
                    @modified_at,
                    @set_is_deleted, @is_deleted,
                    @set_deleted_at, @deleted_at,
                    @set_owner, @owner,
                    @self_identified_user_type,
                    @set_email, @email,
                    @set_ext_ref, @ext_ref)
                """;

            protected override string GetQuery(SelfIdentifiedUserRecord party)
            {
                if (party.SelfIdentifiedUserType.HasValue)
                {
                    return QUERY;
                }

                return base.GetQuery(party);
            }

            protected override void ValidateFields(SelfIdentifiedUserRecord party, PersistenceFeatureFlag[] flags)
            {
                base.ValidateFields(party, flags);
                Debug.Assert(party.SelfIdentifiedUserType.IsSet);

                if (party.SelfIdentifiedUserType.HasValue)
                {
                    switch (party.SelfIdentifiedUserType.Value)
                    {
                        case SelfIdentifiedUserType.IdPortenEmail:
                            Debug.Assert(party.Email.HasValue);
                            Debug.Assert(party.ExtRef.IsNull);
                            break;

                        case SelfIdentifiedUserType.Educational:
                            Debug.Assert(party.Email.IsNull);
                            Debug.Assert(party.ExtRef.HasValue);
                            break;

                        default:
                            Debug.Assert(party.Email.IsNull);
                            Debug.Assert(party.ExtRef.IsNull);
                            break;
                    }
                }
                else
                {
                    Debug.Assert(party.SelfIdentifiedUserType.IsNull);

                    Debug.Assert(party.Email.IsNull);
                    Debug.Assert(party.ExtRef.IsNull);
                }
            }

            protected override void AddPartyParameters(NpgsqlParameterCollection parameters, SelfIdentifiedUserRecord party, PersistenceFeatureFlag[] flags)
            {
                base.AddPartyParameters(parameters, party, flags);

                if (party.SelfIdentifiedUserType.HasValue)
                {
                    parameters.Add<SelfIdentifiedUserType>("self_identified_user_type").TypedValue = party.SelfIdentifiedUserType.Value;
                    parameters.AddOptional("set_email", "email", NpgsqlDbType.Text, party.Email);
                    parameters.AddOptional("set_ext_ref", "ext_ref", NpgsqlDbType.Text, party.ExtRef);
                }
            }

            public override async Task<SelfIdentifiedUserRecord> ReadResult(NpgsqlDataReader reader, CancellationToken cancellationToken)
            {
                // For now, we have to deal with the fact that some SI users do not have the SI table in the DB, so not all the information is always available
                FieldValue<SelfIdentifiedUserType> type = FieldValue.Null;
                FieldValue<string> email = FieldValue.Null;
                FieldValue<string> extRef = FieldValue.Null;

                if (TryGetOrdinal(reader, "p_self_identified_user_type", out var typeOrdinal))
                {
                    type = await reader.GetConditionalFieldValueAsync<SelfIdentifiedUserType>(typeOrdinal, cancellationToken);
                    email = await reader.GetConditionalFieldValueAsync<string>("p_self_identified_email", cancellationToken);
                    extRef = await reader.GetConditionalFieldValueAsync<string>("p_self_identified_ext_ref", cancellationToken);
                }

                return new SelfIdentifiedUserRecord
                {
                    PartyUuid = await reader.GetConditionalFieldValueAsync<Guid>("p_uuid", cancellationToken),
                    OwnerUuid = await reader.GetConditionalFieldValueAsync<Guid>("p_owner", cancellationToken),
                    PartyId = await reader.GetConditionalFieldValueAsync<int>("p_id", cancellationToken).Select(static id => checked((uint)id)),
                    ExternalUrn = await reader.GetConditionalParsableFieldValueAsync<PartyExternalRefUrn>("p_ext_urn", cancellationToken),
                    UserIds = await ReadUserIds(reader, cancellationToken),
                    Usernames = await ReadUsernames(reader, cancellationToken),
                    DisplayName = await reader.GetConditionalFieldValueAsync<string>("p_display_name", cancellationToken),
                    PersonIdentifier = await reader.GetConditionalParsableFieldValueAsync<PersonIdentifier>("p_person_identifier", cancellationToken),
                    OrganizationIdentifier = await reader.GetConditionalParsableFieldValueAsync<OrganizationIdentifier>("p_organization_identifier", cancellationToken),
                    CreatedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>("p_created", cancellationToken),
                    ModifiedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>("p_updated", cancellationToken),
                    IsDeleted = await reader.GetConditionalFieldValueAsync<bool>("p_is_deleted", cancellationToken),
                    DeletedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>("p_deleted_at", cancellationToken),
                    VersionId = await reader.GetConditionalFieldValueAsync<long>("o_version_id", cancellationToken).Select(static v => (ulong)v),

                    SelfIdentifiedUserType = type,
                    Email = email,
                    ExtRef = extRef,
                };
            }
        }

        private sealed class UpsertSystemUserQuery()
            : Typed<SystemUserRecord>(PartyRecordType.SystemUser)
        {
            private const string QUERY =
                /*strpsql*/"""
                SELECT *
                FROM register.upsert_system_user(
                    @flags,
                    @set_uuid, @uuid,
                    @set_id, @id,
                    @ext_urn,
                    @user_ids,
                    @set_username, @username,
                    @party_type,
                    @set_display_name, @display_name,
                    @person_id,
                    @org_id,
                    @created_at,
                    @modified_at,
                    @set_is_deleted, @is_deleted,
                    @set_deleted_at, @deleted_at,
                    @set_owner, @owner,
                    @system_user_type)
                """;

            protected override string GetQuery(SystemUserRecord party)
                => QUERY;

            protected override void ValidateFields(SystemUserRecord party, PersistenceFeatureFlag[] flags)
            {
                base.ValidateFields(party, flags);

                Debug.Assert(party.SystemUserType.HasValue, "system user must have SystemUserType set");
            }

            protected override void AddPartyParameters(NpgsqlParameterCollection parameters, SystemUserRecord party, PersistenceFeatureFlag[] flags)
            {
                base.AddPartyParameters(parameters, party, flags);

                parameters.Add<SystemUserRecordType>("system_user_type").TypedValue = party.SystemUserType.Value;
            }

            public override async Task<SystemUserRecord> ReadResult(NpgsqlDataReader reader, CancellationToken cancellationToken)
            {
                return new SystemUserRecord
                {
                    PartyUuid = await reader.GetConditionalFieldValueAsync<Guid>("p_uuid", cancellationToken),
                    OwnerUuid = await reader.GetConditionalFieldValueAsync<Guid>("p_owner", cancellationToken),
                    PartyId = await reader.GetConditionalFieldValueAsync<int>("p_id", cancellationToken).Select(static id => checked((uint)id)),
                    ExternalUrn = await reader.GetConditionalParsableFieldValueAsync<PartyExternalRefUrn>("p_ext_urn", cancellationToken),
                    UserIds = await ReadUserIds(reader, cancellationToken),
                    Usernames = await ReadUsernames(reader, cancellationToken),
                    DisplayName = await reader.GetConditionalFieldValueAsync<string>("p_display_name", cancellationToken),
                    PersonIdentifier = await reader.GetConditionalParsableFieldValueAsync<PersonIdentifier>("p_person_identifier", cancellationToken),
                    OrganizationIdentifier = await reader.GetConditionalParsableFieldValueAsync<OrganizationIdentifier>("p_organization_identifier", cancellationToken),
                    CreatedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>("p_created", cancellationToken),
                    ModifiedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>("p_updated", cancellationToken),
                    IsDeleted = await reader.GetConditionalFieldValueAsync<bool>("p_is_deleted", cancellationToken),
                    DeletedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>("p_deleted_at", cancellationToken),
                    VersionId = await reader.GetConditionalFieldValueAsync<long>("o_version_id", cancellationToken).Select(static v => (ulong)v),
                    SystemUserType = await reader.GetConditionalFieldValueAsync<SystemUserRecordType>("p_system_user_type", cancellationToken),
                };
            }
        }

        private sealed class UpsertEnterpriseUserQuery()
            : Typed<EnterpriseUserRecord>(PartyRecordType.EnterpriseUser)
        {
            public override async Task<EnterpriseUserRecord> ReadResult(NpgsqlDataReader reader, CancellationToken cancellationToken)
            {
                return new EnterpriseUserRecord
                {
                    PartyUuid = await reader.GetConditionalFieldValueAsync<Guid>("p_uuid", cancellationToken),
                    OwnerUuid = await reader.GetConditionalFieldValueAsync<Guid>("p_owner", cancellationToken),
                    PartyId = await reader.GetConditionalFieldValueAsync<int>("p_id", cancellationToken).Select(static id => checked((uint)id)),
                    ExternalUrn = await reader.GetConditionalParsableFieldValueAsync<PartyExternalRefUrn>("p_ext_urn", cancellationToken),
                    UserIds = await ReadUserIds(reader, cancellationToken),
                    Usernames = await ReadUsernames(reader, cancellationToken),
                    DisplayName = await reader.GetConditionalFieldValueAsync<string>("p_display_name", cancellationToken),
                    PersonIdentifier = await reader.GetConditionalParsableFieldValueAsync<PersonIdentifier>("p_person_identifier", cancellationToken),
                    OrganizationIdentifier = await reader.GetConditionalParsableFieldValueAsync<OrganizationIdentifier>("p_organization_identifier", cancellationToken),
                    CreatedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>("p_created", cancellationToken),
                    ModifiedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>("p_updated", cancellationToken),
                    IsDeleted = await reader.GetConditionalFieldValueAsync<bool>("p_is_deleted", cancellationToken),
                    DeletedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>("p_deleted_at", cancellationToken),
                    VersionId = await reader.GetConditionalFieldValueAsync<long>("o_version_id", cancellationToken).Select(static v => (ulong)v),
                };
            }
        }

        /// <summary>
        /// Singleton <see cref="IAsyncEnumerable{T}"/> for a <see cref="PartyRecord"/>.
        /// </summary>
        internal sealed class AsyncSingleton(PartyRecord party)
            : IAsyncEnumerable<PartyRecord>
            , IAsyncEnumerator<PartyRecord>
        {
            private readonly PartyRecord _party = party;
            private int _index = -1;

            /// <inheritdoc/>
            PartyRecord IAsyncEnumerator<PartyRecord>.Current
                => _party;

            /// <inheritdoc/>
            ValueTask IAsyncDisposable.DisposeAsync()
                => ValueTask.CompletedTask;

            /// <inheritdoc/>
            IAsyncEnumerator<PartyRecord> IAsyncEnumerable<PartyRecord>.GetAsyncEnumerator(CancellationToken cancellationToken)
                => this;

            /// <inheritdoc/>
            ValueTask<bool> IAsyncEnumerator<PartyRecord>.MoveNextAsync()
                => new(Interlocked.Increment(ref _index) == 0);
        }
    }
}
