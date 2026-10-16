using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ModelUtils.EnumUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using CommunityToolkit.Diagnostics;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Register.Persistence;

/// <content>
/// Contains the party query builder.
/// </content>
internal partial class PostgreSqlPartyPersistence
{
    /// <summary>
    /// Query for party records.
    /// </summary>
    [SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1516:Elements should be separated by blank line", Justification = "This class is long enough already")]
    internal sealed class PartyQuery
    {
        private static ConcurrentDictionary<(PartyFieldIncludes Includes, PartyQueryFilters FilterBy), PartyQuery> _queries = new();

        /// <summary>
        /// Gets a cached query for the given includes and filters.
        /// </summary>
        /// <param name="includes">What fields to include.</param>
        /// <param name="filterBy">What to filter by.</param>
        /// <returns>A <see cref="PartyQuery"/>.</returns>
        internal static PartyQuery Get(PartyFieldIncludes includes, PartyQueryFilters filterBy)
        {
            includes |= PartyFieldIncludes.PartyUuid | PartyFieldIncludes.PartyType; // always include the UUID and type

            filterBy.Validate(nameof(filterBy));
            return _queries.GetOrAdd((Includes: includes, FilterBy: filterBy), static (key) => Builder.Create(key.Includes, key.FilterBy));
        }

        private PartyQuery(
            string commandText,
            PartyFields fields,
            FilterParameter paramPartyUuid,
            FilterParameter paramPartyId,
            FilterParameter paramExternalUrn,
            FilterParameter paramPersonIdentifier,
            FilterParameter paramOrganizationIdentifier,
            FilterParameter paramUserId,
            FilterParameter paramUsername,
            FilterParameter paramSelfIdentifiedEmail,
            FilterParameter paramPartyUuidList,
            FilterParameter paramPartyIdList,
            FilterParameter paramExternalUrnList,
            FilterParameter paramPersonIdentifierList,
            FilterParameter paramOrganizationIdentifierList,
            FilterParameter paramUserIdList,
            FilterParameter paramUsernameList,
            FilterParameter paramSelfIdentifiedEmailList,
            FilterParameter paramPartyTypeList,
            FilterParameter paramStreamFrom,
            FilterParameter paramStreamLimit)
        {
            CommandText = commandText;
            _fields = fields;
            _paramPartyUuid = paramPartyUuid;
            _paramPartyId = paramPartyId;
            _paramExternalUrn = paramExternalUrn;
            _paramPersonIdentifier = paramPersonIdentifier;
            _paramOrganizationIdentifier = paramOrganizationIdentifier;
            _paramUserId = paramUserId;
            _paramUsername = paramUsername;
            _paramSelfIdentifiedEmail = paramSelfIdentifiedEmail;
            _paramPartyUuidList = paramPartyUuidList;
            _paramPartyIdList = paramPartyIdList;
            _paramExternalUrnList = paramExternalUrnList;
            _paramPersonIdentifierList = paramPersonIdentifierList;
            _paramOrganizationIdentifierList = paramOrganizationIdentifierList;
            _paramUserIdList = paramUserIdList;
            _paramUsernameList = paramUsernameList;
            _paramSelfIdentifiedEmailList = paramSelfIdentifiedEmailList;
            _paramPartyTypeList = paramPartyTypeList;
            _paramStreamFromExclusive = paramStreamFrom;
            _paramStreamLimit = paramStreamLimit;

            HasSubUnits = _fields.ParentUuid != -1;
        }

        private readonly PartyFields _fields;
        private readonly FilterParameter _paramPartyUuid;
        private readonly FilterParameter _paramPartyId;
        private readonly FilterParameter _paramExternalUrn;
        private readonly FilterParameter _paramPersonIdentifier;
        private readonly FilterParameter _paramOrganizationIdentifier;
        private readonly FilterParameter _paramUserId;
        private readonly FilterParameter _paramUsername;
        private readonly FilterParameter _paramSelfIdentifiedEmail;
        private readonly FilterParameter _paramPartyUuidList;
        private readonly FilterParameter _paramPartyIdList;
        private readonly FilterParameter _paramExternalUrnList;
        private readonly FilterParameter _paramPersonIdentifierList;
        private readonly FilterParameter _paramOrganizationIdentifierList;
        private readonly FilterParameter _paramUserIdList;
        private readonly FilterParameter _paramUsernameList;
        private readonly FilterParameter _paramSelfIdentifiedEmailList;
        private readonly FilterParameter _paramPartyTypeList;
        private readonly FilterParameter _paramStreamFromExclusive;
        private readonly FilterParameter _paramStreamLimit;

        /// <summary>
        /// Gets the command text for the query.
        /// </summary>
        public string CommandText { get; }

        /// <summary>
        /// Gets whether or not the query has sub-units.
        /// </summary>
        public bool HasSubUnits { get; }

        /// <summary>
        /// Adds a party UUID parameter to the command.
        /// </summary>
        public NpgsqlParameter<Guid> AddPartyUuidParameter(NpgsqlCommand cmd, Guid value)
            => AddParameter(cmd, in _paramPartyUuid, value);

        /// <summary>
        /// Adds a party id parameter to the command.
        /// </summary>
        public NpgsqlParameter<long> AddPartyIdParameter(NpgsqlCommand cmd, long value)
            => AddParameter(cmd, in _paramPartyId, value);

        /// <summary>
        /// Adds a external URN parameter to the command.
        /// </summary>
        public NpgsqlParameter<string> AddExternalUrnParameter(NpgsqlCommand cmd, string value)
            => AddParameter(cmd, in _paramExternalUrn, value);

        /// <summary>
        /// Adds a person identifier parameter to the command.
        /// </summary>
        public NpgsqlParameter<string> AddPersonIdentifierParameter(NpgsqlCommand cmd, string value)
            => AddParameter(cmd, in _paramPersonIdentifier, value);

        /// <summary>
        /// Adds a organization identifier parameter to the command.
        /// </summary>
        public NpgsqlParameter<string> AddOrganizationIdentifierParameter(NpgsqlCommand cmd, string value)
            => AddParameter(cmd, in _paramOrganizationIdentifier, value);

        /// <summary>
        /// Adds a user id parameter to the command.
        /// </summary>
        public NpgsqlParameter<long> AddUserIdParameter(NpgsqlCommand cmd, long value)
            => AddParameter(cmd, in _paramUserId, value);

        /// <summary>
        /// Adds a username parameter to the command.
        /// </summary>
        public NpgsqlParameter<string> AddUsernameParameter(NpgsqlCommand cmd, string value)
            => AddParameter(cmd, in _paramUsername, value);

        /// <summary>
        /// Adds a self-identified-user email parameter to the command.
        /// </summary>
        public NpgsqlParameter<string> AddSelfIdentifiedEmail(NpgsqlCommand cmd, string value)
            => AddParameter(cmd, in _paramSelfIdentifiedEmail, value);

        /// <summary>
        /// Adds a party UUID list parameter to the command.
        /// </summary>
        public NpgsqlParameter<IList<Guid>> AddPartyUuidListParameter(NpgsqlCommand cmd, IList<Guid> value)
            => AddParameter(cmd, in _paramPartyUuidList, value);

        /// <summary>
        /// Adds a party id list parameter to the command.
        /// </summary>
        public NpgsqlParameter<IList<long>> AddPartyIdListParameter(NpgsqlCommand cmd, IList<long> value)
            => AddParameter(cmd, in _paramPartyIdList, value);

        /// <summary>
        /// Adds a external URN list parameter to the command.
        /// </summary>
        public NpgsqlParameter<IList<string>> AddExternalUrnListParameter(NpgsqlCommand cmd, IList<string> value)
            => AddParameter(cmd, in _paramExternalUrnList, value);

        /// <summary>
        /// Adds a person identifier list parameter to the command.
        /// </summary>
        public NpgsqlParameter<IList<string>> AddPersonIdentifierListParameter(NpgsqlCommand cmd, IList<string> value)
            => AddParameter(cmd, in _paramPersonIdentifierList, value);

        /// <summary>
        /// Adds a organization identifier list parameter to the command.
        /// </summary>
        public NpgsqlParameter<IList<string>> AddOrganizationIdentifierListParameter(NpgsqlCommand cmd, IList<string> value)
            => AddParameter(cmd, in _paramOrganizationIdentifierList, value);

        /// <summary>
        /// Adds a user id list parameter to the command.
        /// </summary>
        public NpgsqlParameter<IList<long>> AddUserIdListParameter(NpgsqlCommand cmd, IList<long> value)
            => AddParameter(cmd, in _paramUserIdList, value);

        /// <summary>
        /// Adds a username list parameter to the command.
        /// </summary>
        public NpgsqlParameter<IList<string>> AddUsernameListParameter(NpgsqlCommand cmd, IList<string> value)
            => AddParameter(cmd, in _paramUsernameList, value);

        /// <summary>
        /// Adds a self-identified-user email list parameter to the command.
        /// </summary>
        public NpgsqlParameter<IList<string>> AddSelfIdentifiedEmailListParameter(NpgsqlCommand cmd, IList<string> value)
            => AddParameter(cmd, in _paramSelfIdentifiedEmailList, value);

        /// <summary>
        /// Adds a party type list parameter to the command.
        /// </summary>
        public NpgsqlParameter<IList<PartyRecordType>> AddPartyTypeListParameter(NpgsqlCommand cmd, IList<PartyRecordType> value)
            => AddParameter(cmd, in _paramPartyTypeList, value);

        /// <summary>
        /// Adds stream page parameters to the command.
        /// </summary>
        public (NpgsqlParameter<long> From, NpgsqlParameter<int> Limit) AddStreamPageParameters(NpgsqlCommand cmd, ulong fromExclusive, ushort limit)
        {
            var fromParam = AddParameter(cmd, in _paramStreamFromExclusive, (long)fromExclusive);
            var limitParam = AddParameter(cmd, in _paramStreamLimit, (int)limit);

            return (fromParam, limitParam);
        }

        private NpgsqlParameter<T> AddParameter<T>(NpgsqlCommand cmd, in FilterParameter config, T value)
        {
            Debug.Assert(config.HasValue, "Parameter must be configured");
            Debug.Assert(config.Type == typeof(T), "Parameter type mismatch");

            NpgsqlParameter<T> param;

            param = config.DbType.IsDefault()
                ? cmd.Parameters.Add<T>(config.Name)
                : cmd.Parameters.Add<T>(config.Name, config.DbType);

            param.TypedValue = value;
            return param;
        }

        /// <summary>
        /// Reads the parties from a <see cref="NpgsqlCommand"/> asynchronously.
        /// </summary>
        /// <param name="inCmd">The command.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>An async enumerable of <see cref="PartyRecord"/>.</returns>
        public async IAsyncEnumerable<PartyRecord> PrepareAndReadPartiesAsync(NpgsqlCommand inCmd, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Guard.IsNotNull(inCmd);

            await using var cmd = inCmd;

            await cmd.PrepareAsync(cancellationToken);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                yield break;
            }

            do
            {
                var party = await ReadParty(reader, cancellationToken);
                yield return party;
            }
            while (await reader.ReadAsync(cancellationToken));
        }

        private async ValueTask<PartyRecord> ReadParty(NpgsqlDataReader reader, CancellationToken cancellationToken = default)
        {
            var partyType = await reader.GetConditionalFieldValueAsync<PartyRecordType>(_fields.PartyRecordType, cancellationToken);

            return partyType switch
            {
                { HasValue: false } => await ReadBaseParty(reader, _fields, partyType, cancellationToken),
                { Value: PartyRecordType.Person } => await ReadPersonParty(reader, _fields, cancellationToken),
                { Value: PartyRecordType.Organization } => await ReadOrganizationParty(reader, _fields, cancellationToken),
                { Value: PartyRecordType.SelfIdentifiedUser } => await ReadSelfIdentifiedUserParty(reader, _fields, cancellationToken),
                { Value: PartyRecordType.SystemUser } => await ReadSystemUserParty(reader, _fields, cancellationToken),
                { Value: PartyRecordType.EnterpriseUser } => await ReadEnterpriseUserParty(reader, _fields, cancellationToken),
                _ => Unreachable<PartyRecord>(),
            };

            static async ValueTask<PartyRecord> ReadCommonFields(NpgsqlDataReader reader, PartyFields fields, CancellationToken cancellationToken)
            {
                return new PartyRecord(FieldValue.Unset)
                {
                    PartyUuid = await reader.GetConditionalFieldValueAsync<Guid>(fields.PartyUuid, cancellationToken),
                    PartyId = await reader.GetConditionalFieldValueAsync<int>(fields.PartyId, cancellationToken).Select(static id => checked((uint)id)),
                    ExternalUrn = await reader.GetConditionalParsableFieldValueAsync<PartyExternalRefUrn>(fields.PartyExternalUrn, cancellationToken),
                    DisplayName = await reader.GetConditionalFieldValueAsync<string>(fields.PartyDisplayName, cancellationToken),
                    PersonIdentifier = await reader.GetConditionalParsableFieldValueAsync<PersonIdentifier>(fields.PartyPersonIdentifier, cancellationToken),
                    OrganizationIdentifier = await reader.GetConditionalParsableFieldValueAsync<OrganizationIdentifier>(fields.PartyOrganizationIdentifier, cancellationToken),
                    CreatedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>(fields.PartyCreated, cancellationToken),
                    ModifiedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>(fields.PartyUpdated, cancellationToken),
                    IsDeleted = await reader.GetConditionalFieldValueAsync<bool>(fields.PartyIsDeleted, cancellationToken),
                    DeletedAt = await reader.GetConditionalFieldValueAsync<DateTimeOffset>(fields.PartyDeletedAt, cancellationToken),
                    VersionId = await reader.GetConditionalFieldValueAsync<long>(fields.PartyVersionId, cancellationToken).Select(static v => (ulong)v),
                    OwnerUuid = await reader.GetConditionalFieldValueAsync<Guid>(fields.PartyOwnerUuid, cancellationToken),
                    UserIds = await ReadUserIds(reader, fields.ActiveUserId, fields.UserIds, cancellationToken),
                    Usernames = await ReadUsernames(reader, fields.ActiveUsername, fields.Usernames, cancellationToken),
                };
            }

            static async ValueTask<PartyRecord> ReadBaseParty(NpgsqlDataReader reader, PartyFields fields, FieldValue<PartyRecordType> partyType, CancellationToken cancellationToken)
            {
                var common = await ReadCommonFields(reader, fields, cancellationToken);

                // must be the last read-access to the reader
                Debug.Assert(common.PartyUuid.HasValue);

                var party = new PartyRecord(partyType)
                {
                    PartyUuid = common.PartyUuid,
                    PartyId = common.PartyId,
                    ExternalUrn = common.ExternalUrn,
                    DisplayName = common.DisplayName,
                    PersonIdentifier = common.PersonIdentifier,
                    OrganizationIdentifier = common.OrganizationIdentifier,
                    CreatedAt = common.CreatedAt,
                    ModifiedAt = common.ModifiedAt,
                    IsDeleted = common.IsDeleted,
                    DeletedAt = common.DeletedAt,
                    VersionId = common.VersionId,
                    OwnerUuid = common.OwnerUuid,
                    UserIds = common.UserIds,
                    Usernames = common.Usernames,
                };

                return party;
            }

            static async ValueTask<PartyRecord> ReadPersonParty(NpgsqlDataReader reader, PartyFields fields, CancellationToken cancellationToken)
            {
                var common = await ReadCommonFields(reader, fields, cancellationToken);
                var source = await reader.GetConditionalFieldValueAsync<PersonSource>(fields.PersonSource, cancellationToken);
                var firstName = await reader.GetConditionalFieldValueAsync<string>(fields.PersonFirstName, cancellationToken);
                var middleName = await reader.GetConditionalFieldValueAsync<string>(fields.PersonMiddleName, cancellationToken);
                var lastName = await reader.GetConditionalFieldValueAsync<string>(fields.PersonLastName, cancellationToken);
                var shortName = await reader.GetConditionalFieldValueAsync<string>(fields.PersonShortName, cancellationToken);
                var dateOfBirth = await reader.GetConditionalFieldValueAsync<DateOnly>(fields.PersonDateOfBirth, cancellationToken);
                var dateOfDeath = await reader.GetConditionalFieldValueAsync<DateOnly>(fields.PersonDateOfDeath, cancellationToken);
                var address = await reader.GetConditionalFieldValueAsync<StreetAddressRecord>(fields.PersonAddress, cancellationToken);
                var mailingAddress = await reader.GetConditionalFieldValueAsync<MailingAddressRecord>(fields.PersonMailingAddress, cancellationToken);

                // must be the last read-access to the reader
                Debug.Assert(common.PartyUuid.HasValue);

                var party = new PersonRecord
                {
                    PartyUuid = common.PartyUuid,
                    PartyId = common.PartyId,
                    ExternalUrn = common.ExternalUrn,
                    DisplayName = common.DisplayName,
                    PersonIdentifier = common.PersonIdentifier,
                    OrganizationIdentifier = common.OrganizationIdentifier,
                    CreatedAt = common.CreatedAt,
                    ModifiedAt = common.ModifiedAt,
                    IsDeleted = common.IsDeleted,
                    DeletedAt = common.DeletedAt,
                    VersionId = common.VersionId,
                    OwnerUuid = common.OwnerUuid,
                    Source = source,
                    UserIds = common.UserIds,
                    Usernames = common.Usernames,
                    FirstName = firstName,
                    MiddleName = middleName,
                    LastName = lastName,
                    ShortName = shortName,
                    DateOfBirth = dateOfBirth,
                    DateOfDeath = dateOfDeath,
                    Address = address,
                    MailingAddress = mailingAddress,
                };

                return party;
            }

            static async ValueTask<PartyRecord> ReadOrganizationParty(NpgsqlDataReader reader, PartyFields fields, CancellationToken cancellationToken)
            {
                var common = await ReadCommonFields(reader, fields, cancellationToken);
                var source = await reader.GetConditionalFieldValueAsync<OrganizationSource>(fields.OrganizationSource, cancellationToken);
                var parentOrganizationUuid = await reader.GetConditionalFieldValueAsync<Guid>(fields.ParentUuid, cancellationToken);
                var unitStatus = await reader.GetConditionalFieldValueAsync<string>(fields.OrganizationUnitStatus, cancellationToken);
                var unitType = await reader.GetConditionalFieldValueAsync<string>(fields.OrganizationUnitType, cancellationToken);
                var telephoneNumber = await reader.GetConditionalFieldValueAsync<string>(fields.OrganizationTelephoneNumber, cancellationToken);
                var mobileNumber = await reader.GetConditionalFieldValueAsync<string>(fields.OrganizationMobileNumber, cancellationToken);
                var faxNumber = await reader.GetConditionalFieldValueAsync<string>(fields.OrganizationFaxNumber, cancellationToken);
                var emailAddress = await reader.GetConditionalFieldValueAsync<string>(fields.OrganizationEmailAddress, cancellationToken);
                var internetAddress = await reader.GetConditionalFieldValueAsync<string>(fields.OrganizationInternetAddress, cancellationToken);
                var mailingAddress = await reader.GetConditionalFieldValueAsync<MailingAddressRecord>(fields.OrganizationMailingAddress, cancellationToken);
                var businessAddress = await reader.GetConditionalFieldValueAsync<MailingAddressRecord>(fields.OrganizationBusinessAddress, cancellationToken);

                if (parentOrganizationUuid.IsNull)
                {
                    parentOrganizationUuid = FieldValue.Unset;
                }

                // must be the last read-access to the reader
                Debug.Assert(common.PartyUuid.HasValue);

                var party = new OrganizationRecord
                {
                    Source = source,
                    PartyUuid = common.PartyUuid,
                    PartyId = common.PartyId,
                    ExternalUrn = common.ExternalUrn,
                    DisplayName = common.DisplayName,
                    PersonIdentifier = common.PersonIdentifier,
                    OrganizationIdentifier = common.OrganizationIdentifier,
                    CreatedAt = common.CreatedAt,
                    ModifiedAt = common.ModifiedAt,
                    IsDeleted = common.IsDeleted,
                    DeletedAt = common.DeletedAt,
                    VersionId = common.VersionId,
                    OwnerUuid = common.OwnerUuid,
                    UserIds = common.UserIds,
                    Usernames = common.Usernames,
                    UnitStatus = unitStatus,
                    UnitType = unitType,
                    TelephoneNumber = telephoneNumber,
                    MobileNumber = mobileNumber,
                    FaxNumber = faxNumber,
                    EmailAddress = emailAddress,
                    InternetAddress = internetAddress,
                    MailingAddress = mailingAddress,
                    BusinessAddress = businessAddress,
                    ParentOrganizationUuid = parentOrganizationUuid,
                };

                return party;
            }

            static async ValueTask<PartyRecord> ReadSelfIdentifiedUserParty(NpgsqlDataReader reader, PartyFields fields, CancellationToken cancellationToken)
            {
                var common = await ReadCommonFields(reader, fields, cancellationToken);
                var selfIdentifiedUserType = await reader.GetConditionalFieldValueAsync<SelfIdentifiedUserType>(fields.SelfIdentifiedUserType, cancellationToken);
                var email = await reader.GetConditionalFieldValueAsync<string>(fields.SelfIdentifiedUserEmail, cancellationToken);
                var extRef = await reader.GetConditionalFieldValueAsync<string>(fields.SelfIdentifiedUserExtRef, cancellationToken);

                // must be the last read-access to the reader
                Debug.Assert(common.PartyUuid.HasValue);

                var party = new SelfIdentifiedUserRecord
                {
                    PartyUuid = common.PartyUuid,
                    PartyId = common.PartyId,
                    ExternalUrn = common.ExternalUrn,
                    DisplayName = common.DisplayName,
                    PersonIdentifier = common.PersonIdentifier,
                    OrganizationIdentifier = common.OrganizationIdentifier,
                    CreatedAt = common.CreatedAt,
                    ModifiedAt = common.ModifiedAt,
                    IsDeleted = common.IsDeleted,
                    DeletedAt = common.DeletedAt,
                    VersionId = common.VersionId,
                    OwnerUuid = common.OwnerUuid,
                    UserIds = common.UserIds,
                    Usernames = common.Usernames,
                    SelfIdentifiedUserType = selfIdentifiedUserType,
                    Email = email,
                    ExtRef = extRef,
                };

                return party;
            }

            static async ValueTask<PartyRecord> ReadSystemUserParty(NpgsqlDataReader reader, PartyFields fields, CancellationToken cancellationToken)
            {
                var common = await ReadCommonFields(reader, fields, cancellationToken);
                var systemUserType = await reader.GetConditionalFieldValueAsync<SystemUserRecordType>(fields.SystemUserType, cancellationToken);

                // must be the last read-access to the reader
                Debug.Assert(common.PartyUuid.HasValue);

                var party = new SystemUserRecord
                {
                    PartyUuid = common.PartyUuid,
                    PartyId = common.PartyId,
                    ExternalUrn = common.ExternalUrn,
                    DisplayName = common.DisplayName,
                    PersonIdentifier = common.PersonIdentifier,
                    OrganizationIdentifier = common.OrganizationIdentifier,
                    CreatedAt = common.CreatedAt,
                    ModifiedAt = common.ModifiedAt,
                    IsDeleted = common.IsDeleted,
                    DeletedAt = common.DeletedAt,
                    VersionId = common.VersionId,
                    OwnerUuid = common.OwnerUuid,
                    UserIds = common.UserIds,
                    Usernames = common.Usernames,
                    SystemUserType = systemUserType,
                };

                return party;
            }

            static async ValueTask<PartyRecord> ReadEnterpriseUserParty(NpgsqlDataReader reader, PartyFields fields, CancellationToken cancellationToken)
            {
                var common = await ReadCommonFields(reader, fields, cancellationToken);

                // must be the last read-access to the reader
                Debug.Assert(common.PartyUuid.HasValue);

                var party = new EnterpriseUserRecord
                {
                    PartyUuid = common.PartyUuid,
                    PartyId = common.PartyId,
                    ExternalUrn = common.ExternalUrn,
                    DisplayName = common.DisplayName,
                    PersonIdentifier = common.PersonIdentifier,
                    OrganizationIdentifier = common.OrganizationIdentifier,
                    CreatedAt = common.CreatedAt,
                    ModifiedAt = common.ModifiedAt,
                    IsDeleted = common.IsDeleted,
                    DeletedAt = common.DeletedAt,
                    VersionId = common.VersionId,
                    OwnerUuid = common.OwnerUuid,
                    UserIds = common.UserIds,
                    Usernames = common.Usernames,
                };

                return party;
            }

            static async ValueTask<FieldValue<PartyHistoricalAggregate<uint>>> ReadUserIds(NpgsqlDataReader reader, int activeUserIdIndex, int userIdsIndex, CancellationToken cancellationToken)
            {
                if (activeUserIdIndex == -1 || userIdsIndex == -1)
                {
                    Debug.Assert(activeUserIdIndex == -1 && userIdsIndex == -1);
                    return FieldValue.Unset;
                }

                var activeValue = await reader.GetConditionalFieldValueAsync<long>(activeUserIdIndex, cancellationToken);
                var values = await reader.GetConditionalFieldValueAsync<long[]>(userIdsIndex, cancellationToken);

                if (values.IsNull || values.Value!.Length == 0)
                {
                    Debug.Assert(activeValue.IsNull);
                    return PartyHistoricalAggregate<uint>.Empty;
                }

                Debug.Assert(activeValue.IsSet);
                Debug.Assert(values.HasValue);

                var hasActive = activeValue.HasValue;
                return PartyHistoricalAggregate<uint>.Create(values.Value.Select(static id => checked((uint)id)), hasActive);
            }

            static async ValueTask<FieldValue<PartyHistoricalAggregate<string>>> ReadUsernames(NpgsqlDataReader reader, int activeUsernameIndex, int usernamesIndex, CancellationToken cancellationToken)
            {
                if (activeUsernameIndex == -1 || usernamesIndex == -1)
                {
                    Debug.Assert(activeUsernameIndex == -1 && usernamesIndex == -1);
                    return FieldValue.Unset;
                }

                var activeValue = await reader.GetConditionalFieldValueAsync<string>(activeUsernameIndex, cancellationToken);
                var values = await reader.GetConditionalFieldValueAsync<string[]>(usernamesIndex, cancellationToken);

                if (values.IsNull || values.Value!.Length == 0)
                {
                    Debug.Assert(activeValue.IsNull);
                    return PartyHistoricalAggregate<string>.Empty;
                }

                Debug.Assert(activeValue.IsSet);
                Debug.Assert(values.HasValue);

                var hasActive = activeValue.HasValue;
                return PartyHistoricalAggregate<string>.Create(values.Value, hasActive);
            }

            [DoesNotReturn]
            [MethodImpl(MethodImplOptions.NoInlining)]
            static T Unreachable<T>()
                => throw new UnreachableException();
        }

        private sealed class Builder
        {
            public static PartyQuery Create(PartyFieldIncludes includes, PartyQueryFilters filterBy)
            {
                Builder builder = new();
                builder.Populate(includes, filterBy);

                PartyFields fields = new(
                    parentUuid: builder._parentUuid,
                    partyUuid: builder._partyUuid,
                    partyId: builder._partyId,
                    partyType: builder._partyType,
                    partyExternalUrn: builder._partyExternalUrn,
                    partyDisplayName: builder._partyDisplayName,
                    partyPersonIdentifier: builder._partyPersonIdentifier,
                    partyOrganizationIdentifier: builder._partyOrganizationIdentifier,
                    partyCreated: builder._partyCreated,
                    partyUpdated: builder._partyUpdated,
                    partyIsDeleted: builder._partyIsDeleted,
                    partyDeletedAt: builder._partyDeletedAt,
                    partyVersionId: builder._partyVersionId,
                    partyOwnerUuid: builder._partyOwnerUuid,
                    personFirstName: builder._personFirstName,
                    personMiddleName: builder._personMiddleName,
                    personLastName: builder._personLastName,
                    personShortName: builder._personShortName,
                    personDateOfBirth: builder._personDateOfBirth,
                    personDateOfDeath: builder._personDateOfDeath,
                    personAddress: builder._personAddress,
                    personMailingAddress: builder._personMailingAddress,
                    personSource: builder._personSource,
                    organizationUnitStatus: builder._organizationUnitStatus,
                    organizationUnitType: builder._organizationUnitType,
                    organizationTelephoneNumber: builder._organizationTelephoneNumber,
                    organizationMobileNumber: builder._organizationMobileNumber,
                    organizationFaxNumber: builder._organizationFaxNumber,
                    organizationEmailAddress: builder._organizationEmailAddress,
                    organizationInternetAddress: builder._organizationInternetAddress,
                    organizationMailingAddress: builder._organizationMailingAddress,
                    organizationBusinessAddress: builder._organizationBusinessAddress,
                    organizationSource: builder._organizationSource,
                    selfIdentifiedUserType: builder._selfIdentifiedUserType,
                    selfIdentifiedUserEmail: builder._selfIdentifiedUserEmail,
                    selfIdentifiedUserExtRef: builder._selfIdentifiedUserExtRef,
                    systemUserType: builder._systemUserType,
                    activeUserId: builder._activeUserId,
                    userIds: builder._userIds,
                    activeUsername: builder._activeUsername,
                    usernames: builder._usernames);

                var commandText = builder._builder.ToString();
                return new(
                    commandText,
                    fields,
                    paramPartyUuid: builder._paramPartyUuid,
                    paramPartyId: builder._paramPartyId,
                    paramExternalUrn: builder._paramExternalUrn,
                    paramPersonIdentifier: builder._paramPersonIdentifier,
                    paramOrganizationIdentifier: builder._paramOrganizationIdentifier,
                    paramUserId: builder._paramUserId,
                    paramUsername: builder._paramUsername,
                    paramSelfIdentifiedEmail: builder._paramSelfIdentifiedEmail,
                    paramPartyUuidList: builder._paramPartyUuidList,
                    paramPartyIdList: builder._paramPartyIdList,
                    paramExternalUrnList: builder._paramExternalUrnList,
                    paramPersonIdentifierList: builder._paramPersonIdentifierList,
                    paramOrganizationIdentifierList: builder._paramOrganizationIdentifierList,
                    paramUserIdList: builder._paramUserIdList,
                    paramUsernameList: builder._paramUsernameList,
                    paramSelfIdentifiedEmailList: builder._paramSelfIdentifiedEmailList,
                    paramPartyTypeList: builder._paramPartyTypeList,
                    paramStreamFrom: builder._paramStreamFromExclusive,
                    paramStreamLimit: builder._paramStreamLimit);
            }

            private readonly StringBuilder _builder = new();

            // parameters
            private FilterParameter _paramPartyUuid;
            private FilterParameter _paramPartyId;
            private FilterParameter _paramExternalUrn;
            private FilterParameter _paramPersonIdentifier;
            private FilterParameter _paramOrganizationIdentifier;
            private FilterParameter _paramUserId;
            private FilterParameter _paramUsername;
            private FilterParameter _paramSelfIdentifiedEmail;
            private FilterParameter _paramPartyUuidList;
            private FilterParameter _paramPartyIdList;
            private FilterParameter _paramExternalUrnList;
            private FilterParameter _paramPersonIdentifierList;
            private FilterParameter _paramOrganizationIdentifierList;
            private FilterParameter _paramUserIdList;
            private FilterParameter _paramUsernameList;
            private FilterParameter _paramSelfIdentifiedEmailList;
            private FilterParameter _paramPartyTypeList;
            private FilterParameter _paramStreamFromExclusive;
            private FilterParameter _paramStreamLimit;

            // fields
            private sbyte _fieldIndex = 0;

            // meta fields
            private sbyte _parentUuid = -1;

            // register.party
            private sbyte _partyUuid = -1;
            private sbyte _partyId = -1;
            private sbyte _partyType = -1;
            private sbyte _partyExternalUrn = -1;
            private sbyte _partyDisplayName = -1;
            private sbyte _partyPersonIdentifier = -1;
            private sbyte _partyOrganizationIdentifier = -1;
            private sbyte _partyCreated = -1;
            private sbyte _partyUpdated = -1;
            private sbyte _partyIsDeleted = -1;
            private sbyte _partyDeletedAt = -1;
            private sbyte _partyVersionId = -1;
            private sbyte _partyOwnerUuid = -1;

            // register.person
            private sbyte _personFirstName = -1;
            private sbyte _personMiddleName = -1;
            private sbyte _personLastName = -1;
            private sbyte _personShortName = -1;
            private sbyte _personDateOfBirth = -1;
            private sbyte _personDateOfDeath = -1;
            private sbyte _personAddress = -1;
            private sbyte _personMailingAddress = -1;
            private sbyte _personSource = -1;

            // register.organization
            private sbyte _organizationUnitStatus = -1;
            private sbyte _organizationUnitType = -1;
            private sbyte _organizationTelephoneNumber = -1;
            private sbyte _organizationMobileNumber = -1;
            private sbyte _organizationFaxNumber = -1;
            private sbyte _organizationEmailAddress = -1;
            private sbyte _organizationInternetAddress = -1;
            private sbyte _organizationMailingAddress = -1;
            private sbyte _organizationBusinessAddress = -1;
            private sbyte _organizationSource = -1;

            // register.self_identified_user
            private sbyte _selfIdentifiedUserType = -1;
            private sbyte _selfIdentifiedUserEmail = -1;
            private sbyte _selfIdentifiedUserExtRef = -1;

            // register.system_user
            private sbyte _systemUserType = -1;

            // register.user
            private sbyte _activeUserId = -1;
            private sbyte _userIds = -1;

            // register.username
            private sbyte _activeUsername = -1;
            private sbyte _usernames = -1;

            public void Populate(PartyFieldIncludes includes, PartyQueryFilters filterBy)
            {
                PopulateCommonTableExpressions(includes, filterBy);

                _builder.Append(/*strpsql*/"SELECT");

                _parentUuid = AddField("uuids.parent_uuid", "p_parent_uuid", include: filterBy.Transforms.HasFlag(PartyListTransforms.IncludeSubUnits));

                _partyUuid = AddField("party.uuid", "p_uuid", includes.HasFlag(PartyFieldIncludes.PartyUuid));
                _partyId = AddField("party.id", "p_id", includes.HasFlag(PartyFieldIncludes.PartyId));
                _partyType = AddField("party.party_type", "p_party_type", includes.HasFlag(PartyFieldIncludes.PartyType));
                _partyExternalUrn = AddField("party.ext_urn", "p_ext_urn", includes.HasFlag(PartyFieldIncludes.PartyExternalUrn));
                _partyDisplayName = AddField("party.display_name", "p_display_name", includes.HasFlag(PartyFieldIncludes.PartyDisplayName));
                _partyPersonIdentifier = AddField("party.person_identifier", "p_person_identifier", includes.HasFlag(PartyFieldIncludes.PartyPersonIdentifier));
                _partyOrganizationIdentifier = AddField("party.organization_identifier", "p_organization_identifier", includes.HasFlag(PartyFieldIncludes.PartyOrganizationIdentifier));
                _partyCreated = AddField("party.created", "p_created", includes.HasFlag(PartyFieldIncludes.PartyCreatedAt));
                _partyUpdated = AddField("party.updated", "p_updated", includes.HasFlag(PartyFieldIncludes.PartyModifiedAt));
                _partyIsDeleted = AddField("party.is_deleted", "p_is_deleted", includes.HasFlag(PartyFieldIncludes.PartyIsDeleted));
                _partyDeletedAt = AddField("party.deleted_at", "p_deleted_at", includes.HasFlag(PartyFieldIncludes.PartyDeletedAt));
                _partyVersionId = AddField("party.version_id", "p_version_id", includes.HasFlag(PartyFieldIncludes.PartyVersionId));
                _partyOwnerUuid = AddField("party.\"owner\"", "p_owner_uuid", includes.HasFlag(PartyFieldIncludes.PartyOwnerUuid));

                _personFirstName = AddField("person.first_name", "p_first_name", includes.HasFlag(PartyFieldIncludes.PersonFirstName));
                _personMiddleName = AddField("person.middle_name", "p_middle_name", includes.HasFlag(PartyFieldIncludes.PersonMiddleName));
                _personLastName = AddField("person.last_name", "p_last_name", includes.HasFlag(PartyFieldIncludes.PersonLastName));
                _personShortName = AddField("person.short_name", "p_short_name", includes.HasFlag(PartyFieldIncludes.PersonShortName));
                _personDateOfBirth = AddField("person.date_of_birth", "p_date_of_birth", includes.HasFlag(PartyFieldIncludes.PersonDateOfBirth));
                _personDateOfDeath = AddField("person.date_of_death", "p_date_of_death", includes.HasFlag(PartyFieldIncludes.PersonDateOfDeath));
                _personAddress = AddField("person.address", "p_address", includes.HasFlag(PartyFieldIncludes.PersonAddress));
                _personMailingAddress = AddField("person.mailing_address", "p_person_mailing_address", includes.HasFlag(PartyFieldIncludes.PersonMailingAddress));
                _personSource = AddField("person.source", "p_person_source", includes.HasFlag(PartyFieldIncludes.PersonSource));

                _organizationUnitStatus = AddField("org.unit_status", "p_unit_status", includes.HasFlag(PartyFieldIncludes.OrganizationUnitStatus));
                _organizationUnitType = AddField("org.unit_type", "p_unit_type", includes.HasFlag(PartyFieldIncludes.OrganizationUnitType));
                _organizationTelephoneNumber = AddField("org.telephone_number", "p_telephone_number", includes.HasFlag(PartyFieldIncludes.OrganizationTelephoneNumber));
                _organizationMobileNumber = AddField("org.mobile_number", "p_mobile_number", includes.HasFlag(PartyFieldIncludes.OrganizationMobileNumber));
                _organizationFaxNumber = AddField("org.fax_number", "p_fax_number", includes.HasFlag(PartyFieldIncludes.OrganizationFaxNumber));
                _organizationEmailAddress = AddField("org.email_address", "p_email_address", includes.HasFlag(PartyFieldIncludes.OrganizationEmailAddress));
                _organizationInternetAddress = AddField("org.internet_address", "p_internet_address", includes.HasFlag(PartyFieldIncludes.OrganizationInternetAddress));
                _organizationMailingAddress = AddField("org.mailing_address", "p_org_mailing_address", includes.HasFlag(PartyFieldIncludes.OrganizationMailingAddress));
                _organizationBusinessAddress = AddField("org.business_address", "p_business_address", includes.HasFlag(PartyFieldIncludes.OrganizationBusinessAddress));
                _organizationSource = AddField("org.source", "p_organization_source", includes.HasFlag(PartyFieldIncludes.OrganizationSource));

                _selfIdentifiedUserType = AddField("si_u.\"type\"", "p_self_identified_user_type", includes.HasFlag(PartyFieldIncludes.SelfIdentifiedUserType));
                _selfIdentifiedUserEmail = AddField("si_u.email", "p_self_identified_user_email", includes.HasFlag(PartyFieldIncludes.SelfIdentifiedUserEmail));
                _selfIdentifiedUserExtRef = AddField("si_u.ext_ref", "p_self_identified_user_ext_ref", includes.HasFlag(PartyFieldIncludes.SelfIdentifiedUserExtRef));

                _systemUserType = AddField("sys_u.\"type\"", "p_system_user_type", includes.HasFlag(PartyFieldIncludes.SystemUserType));

                _activeUserId = AddField("agg_uid.user_id", "u_user_id", includes.HasFlag(PartyFieldIncludes.UserId));
                _userIds = AddField("agg_uid.user_ids", "u_user_ids", includes.HasFlag(PartyFieldIncludes.UserId));

                _activeUsername = AddField("agg_uname.username", "u_username", includes.HasFlag(PartyFieldIncludes.Username));
                _usernames = AddField("agg_uname.usernames", "u_usernames", includes.HasFlag(PartyFieldIncludes.Username));

                _builder.AppendLine().Append(/*strpsql*/"FROM uuids AS uuids");
                _builder.AppendLine().Append(/*strpsql*/"INNER JOIN register.party AS party USING (uuid)");

                if (includes.HasAnyFlags(PartyFieldIncludes.Person))
                {
                    _builder.AppendLine().Append(/*strpsql*/"LEFT JOIN register.person AS person USING (uuid)");
                }

                if (includes.HasAnyFlags(PartyFieldIncludes.Organization))
                {
                    _builder.AppendLine().Append(/*strpsql*/"LEFT JOIN register.organization AS org USING (uuid)");
                }

                if (includes.HasAnyFlags(PartyFieldIncludes.SelfIdentifiedUser))
                {
                    _builder.AppendLine().Append(/*strpsql*/"""LEFT JOIN register.self_identified_user AS si_u USING (uuid)""");
                }

                if (includes.HasAnyFlags(PartyFieldIncludes.SystemUser))
                {
                    _builder.AppendLine().Append(/*strpsql*/"""LEFT JOIN register.system_user AS sys_u USING (uuid)""");
                }

                if (includes.HasFlag(PartyFieldIncludes.UserId))
                {
                    _builder.AppendLine().Append(/*strpsql*/"""LEFT JOIN aggregated_user_ids AS agg_uid USING (uuid)""");
                }

                if (includes.HasFlag(PartyFieldIncludes.Username))
                {
                    _builder.AppendLine().Append(/*strpsql*/"""LEFT JOIN aggregated_usernames AS agg_uname USING (uuid)""");
                }

                _builder.AppendLine().AppendLine(/*strpsql*/"ORDER BY").Append(/*strpsql*/"    uuids.sort_first");
                _builder.AppendLine(",").Append(/*strpsql*/"    uuids.sort_second NULLS FIRST");
            }

            private void PopulateCommonTableExpressions(PartyFieldIncludes includes, PartyQueryFilters filterBy)
            {
                const string TOP_LEVEL_UUIDS = "uuids";
                const string TOP_LEVEL_UNTRANSFORMED = "top_level_uuids_untransformed";
                const string TOP_LEVEL_UNFILTERED = "top_level_uuids_unfiltered";

                var firstExpression = true;
                switch (filterBy.Mode)
                {
                    case PartyQueryFilters.QueryMode.LookupOne:
                        Debug.Assert(filterBy.Transforms is PartyListTransforms.None);
                        PopulateLookupOneCommonTableExpression(name: TOP_LEVEL_UNTRANSFORMED, filterBy.LookupIdentifiers, ref firstExpression);
                        PopulateListTransformCommonTableExpression(name: TOP_LEVEL_UUIDS, source: TOP_LEVEL_UNTRANSFORMED, transforms: PartyListTransforms.None, ref firstExpression);
                        break;

                    case PartyQueryFilters.QueryMode.LookupMultiple:
                        var hasFilters = filterBy.ListFilters is not PartyListFilters.None;

                        var cteName = TOP_LEVEL_UNTRANSFORMED;
                        PopulateLookupMultipleCommonTableExpression(name: TOP_LEVEL_UNTRANSFORMED, filterBy.LookupIdentifiers, ref firstExpression);

                        var nextName = !hasFilters ? TOP_LEVEL_UUIDS : TOP_LEVEL_UNFILTERED;
                        PopulateListTransformCommonTableExpression(name: nextName, source: cteName, transforms: filterBy.Transforms, ref firstExpression);
                        cteName = nextName;

                        if (hasFilters)
                        {
                            PopulateListFilterCommonTableExpression(name: TOP_LEVEL_UUIDS, source: cteName, filterBy.ListFilters, streamPage: false, ref firstExpression);
                        }

                        break;

                    case PartyQueryFilters.QueryMode.FilteredStream:
                        Debug.Assert(filterBy.Transforms is PartyListTransforms.None);
                        PopulateListFilterCommonTableExpression(name: TOP_LEVEL_UNTRANSFORMED, source: null, filterBy.ListFilters, streamPage: true, ref firstExpression);
                        PopulateListTransformCommonTableExpression(name: TOP_LEVEL_UUIDS, source: TOP_LEVEL_UNTRANSFORMED, transforms: PartyListTransforms.None, ref firstExpression);
                        break;

                    default:
                        ThrowHelper.ThrowArgumentOutOfRangeException(nameof(filterBy), filterBy, "Unsupported filter mode");
                        break;
                }

                if (includes.HasFlag(PartyFieldIncludes.UserId))
                {
                    switch ((filterBy.LookupIdentifiers.HasFlag(PartyLookupIdentifiers.UserId), filterBy.Mode))
                    {
                        case (false, _):
                            AddCommonTableExpression(
                                ref firstExpression,
                                "filtered_user_ids",
                                /*strpsql*/"""
                                SELECT "user".*
                                FROM register."user" AS "user"
                                WHERE "user".is_active
                                """);
                            break;

                        case (true, PartyQueryFilters.QueryMode.LookupOne):
                            AddCommonTableExpression(
                                ref firstExpression,
                                "filtered_user_ids",
                                /*strpsql*/"""
                                SELECT "user".*
                                FROM register."user" AS "user"
                                WHERE "user".is_active
                                   OR "user".user_id = @userId
                                """);
                            break;

                        default:
                            Debug.Assert(_paramUserIdList.HasValue);
                            AddCommonTableExpression(
                                ref firstExpression,
                                "filtered_user_ids",
                                /*strpsql*/"""
                                SELECT "user".*
                                FROM register."user" AS "user"
                                WHERE "user".is_active
                                   OR "user".user_id = ANY (@userIds)
                                """);
                            break;
                    }
                }

                if (includes.HasFlag(PartyFieldIncludes.Username))
                {
                    switch ((filterBy.LookupIdentifiers.HasFlag(PartyLookupIdentifiers.Username), filterBy.Mode))
                    {
                        case (false, _):
                            AddCommonTableExpression(
                                ref firstExpression,
                                "filtered_usernames",
                                /*strpsql*/"""
                                SELECT "username".*
                                FROM register."username" AS "username"
                                WHERE "username".is_active
                                """);
                            break;

                        case (true, PartyQueryFilters.QueryMode.LookupOne):
                            AddCommonTableExpression(
                                ref firstExpression,
                                "filtered_usernames",
                                /*strpsql*/"""
                                SELECT "username".*
                                FROM register."username" AS "username"
                                WHERE "username".is_active
                                   OR "username".username = @username
                                """);
                            break;

                        default:
                            Debug.Assert(_paramUsernameList.HasValue);
                            AddCommonTableExpression(
                                ref firstExpression,
                                "filtered_usernames",
                                /*strpsql*/"""
                                SELECT "username".*
                                FROM register."username" AS "username"
                                WHERE "username".is_active
                                   OR "username".username = ANY (@usernames)
                                """);
                            break;
                    }
                }

                if (includes.HasFlag(PartyFieldIncludes.UserId))
                {
                    // note: user_id will be NULL if there is no active user
                    AddCommonTableExpression(
                        ref firstExpression,
                        "aggregated_user_ids",
                        /*strpsql*/"""
                        SELECT
                            uuid,
                            max(user_id) FILTER (WHERE is_active) as user_id,
                            array_agg(user_id ORDER BY is_active DESC, user_id DESC) as user_ids
                        FROM filtered_user_ids
                        INNER JOIN uuids USING (uuid)
                        GROUP BY uuid
                        """);
                }

                if (includes.HasFlag(PartyFieldIncludes.Username))
                {
                    // note: username will be NULL if there is no active username
                    AddCommonTableExpression(
                        ref firstExpression,
                        "aggregated_usernames",
                        /*strpsql*/"""
                        SELECT
                            uuid,
                            max(username) FILTER (WHERE is_active) as username,
                            array_agg(username ORDER BY is_active DESC, username) as usernames
                        FROM filtered_usernames
                        INNER JOIN uuids USING (uuid)
                        GROUP BY uuid
                        """);
                }

                _builder.AppendLine();
            }

            private void PopulateLookupOneCommonTableExpression(string name, PartyLookupIdentifiers identifier, ref bool firstExpression)
            {
                // if we are not filtering on multiple values, we only allow a single filter type
                switch (identifier)
                {
                    case PartyLookupIdentifiers.PartyUuid:
                        _paramPartyUuid = new(typeof(Guid), "partyUuid", NpgsqlDbType.Uuid);
                        AddCommonTableExpression(
                            ref firstExpression,
                            name,
                            /*strpsql*/"""
                                SELECT party."uuid", party.version_id
                                FROM register.party AS party
                                WHERE party."uuid" = @partyUuid
                                """);
                        break;

                    case PartyLookupIdentifiers.PartyId:
                        _paramPartyId = new(typeof(long), "partyId", NpgsqlDbType.Bigint);
                        AddCommonTableExpression(
                            ref firstExpression,
                            name,
                            /*strpsql*/"""
                                SELECT party."uuid", party.version_id
                                FROM register.party AS party
                                WHERE party."id" = @partyId
                                """);
                        break;

                    case PartyLookupIdentifiers.ExternalUrn:
                        _paramExternalUrn = new(typeof(string), "externalUrn", NpgsqlDbType.Text);
                        AddCommonTableExpression(
                            ref firstExpression,
                            name,
                            /*strpsql*/"""
                                SELECT party."uuid", party.version_id
                                FROM register.party AS party
                                WHERE party."ext_urn" = @externalUrn
                                """);
                        break;

                    case PartyLookupIdentifiers.PersonIdentifier:
                        _paramPersonIdentifier = new(typeof(string), "personIdentifier", NpgsqlDbType.Text);
                        AddCommonTableExpression(
                            ref firstExpression,
                            name,
                            /*strpsql*/"""
                                SELECT party."uuid", party.version_id
                                FROM register.party AS party
                                WHERE party."person_identifier" = @personIdentifier
                                """);
                        break;

                    case PartyLookupIdentifiers.OrganizationIdentifier:
                        _paramOrganizationIdentifier = new(typeof(string), "organizationIdentifier", NpgsqlDbType.Text);
                        AddCommonTableExpression(
                            ref firstExpression,
                            name,
                            /*strpsql*/"""
                                SELECT party."uuid", party.version_id
                                FROM register.party AS party
                                WHERE party."organization_identifier" = @organizationIdentifier
                                """);
                        break;

                    case PartyLookupIdentifiers.UserId:
                        _paramUserId = new(typeof(long), "userId", NpgsqlDbType.Bigint);
                        AddCommonTableExpression(
                            ref firstExpression,
                            name,
                            /*strpsql*/"""
                                SELECT "user"."uuid", party.version_id
                                FROM register."user" AS "user"
                                INNER JOIN register.party AS party USING (uuid)
                                WHERE "user".user_id = @userId
                                """);
                        break;

                    case PartyLookupIdentifiers.Username:
                        _paramUsername = new(typeof(string), "username", NpgsqlDbType.Text);
                        AddCommonTableExpression(
                            ref firstExpression,
                            name,
                            /*strpsql*/"""
                                SELECT "username"."uuid", party.version_id
                                FROM register."username" AS "username"
                                INNER JOIN register.party AS party USING (uuid)
                                WHERE "username".username = @username
                                """);
                        break;

                    case PartyLookupIdentifiers.SelfIdentifiedEmail:
                        _paramSelfIdentifiedEmail = new(typeof(string), "selfIdentifiedEmail", NpgsqlDbType.Text);
                        AddCommonTableExpression(
                            ref firstExpression,
                            name,
                            /*strpsql*/"""
                                SELECT si_u."uuid", party.version_id
                                FROM register.self_identified_user AS si_u
                                INNER JOIN register.party AS party USING (uuid)
                                WHERE si_u.email = @selfIdentifiedEmail
                                """);
                        break;
                }

                Debug.Assert(!firstExpression);
            }

            private void PopulateLookupMultipleCommonTableExpression(string name, PartyLookupIdentifiers identifier, ref bool firstExpression)
            {
                var idSets = new List<string>();

                if (identifier.HasFlag(PartyLookupIdentifiers.PartyUuid))
                {
                    // TODO: https://github.com/npgsql/npgsql/issues/5655 - change to IReadOnlyList when Npgsql supports it
                    _paramPartyUuidList = new(typeof(IList<Guid>), "partyUuids", NpgsqlDbType.Array | NpgsqlDbType.Uuid);
                    idSets.Add("uuids_by_party_uuid");
                    AddCommonTableExpression(
                        ref firstExpression,
                        "uuids_by_party_uuid",
                        /*strpsql*/"""
                            SELECT party."uuid", party.version_id
                            FROM register.party AS party
                            WHERE party."uuid" = ANY (@partyUuids)
                            """);
                }

                if (identifier.HasFlag(PartyLookupIdentifiers.PartyId))
                {
                    // TODO: https://github.com/npgsql/npgsql/issues/5655 - change to IReadOnlyList when Npgsql supports it
                    _paramPartyIdList = new(typeof(IList<long>), "partyIds", NpgsqlDbType.Array | NpgsqlDbType.Bigint);
                    idSets.Add("uuids_by_party_id");
                    AddCommonTableExpression(
                        ref firstExpression,
                        "uuids_by_party_id",
                        /*strpsql*/"""
                            SELECT party."uuid", party.version_id
                            FROM register.party AS party
                            WHERE party."id" = ANY (@partyIds)
                            """);
                }

                if (identifier.HasFlag(PartyLookupIdentifiers.ExternalUrn))
                {
                    // TODO: https://github.com/npgsql/npgsql/issues/5655 - change to IReadOnlyList when Npgsql supports it
                    _paramExternalUrnList = new(typeof(IList<string>), "externalUrns", NpgsqlDbType.Array | NpgsqlDbType.Text);
                    idSets.Add("uuids_by_external_urn");
                    AddCommonTableExpression(
                        ref firstExpression,
                        "uuids_by_external_urn",
                        /*strpsql*/"""
                            SELECT party."uuid", party.version_id
                            FROM register.party AS party
                            WHERE party."ext_urn" = ANY (@externalUrns)
                            """);
                }

                if (identifier.HasFlag(PartyLookupIdentifiers.PersonIdentifier))
                {
                    // TODO: https://github.com/npgsql/npgsql/issues/5655 - change to IReadOnlyList when Npgsql supports it
                    _paramPersonIdentifierList = new(typeof(IList<string>), "personIdentifiers", NpgsqlDbType.Array | NpgsqlDbType.Text);
                    idSets.Add("uuids_by_person_identifier");
                    AddCommonTableExpression(
                        ref firstExpression,
                        "uuids_by_person_identifier",
                        /*strpsql*/"""
                            SELECT party."uuid", party.version_id
                            FROM register.party AS party
                            WHERE party."person_identifier" = ANY (@personIdentifiers)
                            """);
                }

                if (identifier.HasFlag(PartyLookupIdentifiers.OrganizationIdentifier))
                {
                    // TODO: https://github.com/npgsql/npgsql/issues/5655 - change to IReadOnlyList when Npgsql supports it
                    _paramOrganizationIdentifierList = new(typeof(IList<string>), "organizationIdentifiers", NpgsqlDbType.Array | NpgsqlDbType.Text);
                    idSets.Add("uuids_by_organization_identifier");
                    AddCommonTableExpression(
                        ref firstExpression,
                        "uuids_by_organization_identifier",
                        /*strpsql*/"""
                            SELECT party."uuid", party.version_id
                            FROM register.party AS party
                            WHERE party."organization_identifier" = ANY (@organizationIdentifiers)
                            """);
                }

                if (identifier.HasFlag(PartyLookupIdentifiers.UserId))
                {
                    // TODO: https://github.com/npgsql/npgsql/issues/5655 - change to IReadOnlyList when Npgsql supports it
                    _paramUserIdList = new(typeof(IList<long>), "userIds", NpgsqlDbType.Array | NpgsqlDbType.Bigint);
                    idSets.Add("uuids_by_user_id");
                    AddCommonTableExpression(
                        ref firstExpression,
                        "uuids_by_user_id",
                        /*strpsql*/"""
                            SELECT "user"."uuid", party.version_id
                            FROM register."user" AS "user"
                            INNER JOIN register.party AS party USING (uuid)
                            WHERE "user".user_id = ANY (@userIds)
                            """);
                }

                if (identifier.HasFlag(PartyLookupIdentifiers.Username))
                {
                    // TODO: https://github.com/npgsql/npgsql/issues/5655 - change to IReadOnlyList when Npgsql supports it
                    _paramUsernameList = new(typeof(IList<string>), "usernames", NpgsqlDbType.Array | NpgsqlDbType.Text);
                    idSets.Add("uuids_by_username");
                    AddCommonTableExpression(
                        ref firstExpression,
                        "uuids_by_username",
                        /*strpsql*/"""
                            SELECT "username"."uuid", party.version_id
                            FROM register."username" AS "username"
                            INNER JOIN register.party AS party USING (uuid)
                            WHERE "username".username = ANY (@usernames)
                            """);
                }

                if (identifier.HasFlag(PartyLookupIdentifiers.SelfIdentifiedEmail))
                {
                    // TODO: https://github.com/npgsql/npgsql/issues/5655 - change to IReadOnlyList when Npgsql supports it
                    _paramSelfIdentifiedEmailList = new(typeof(IList<string>), "selfIdentifiedEmails", NpgsqlDbType.Array | NpgsqlDbType.Text);
                    idSets.Add("uuids_by_self_identified_email");
                    AddCommonTableExpression(
                        ref firstExpression,
                        "uuids_by_self_identified_email",
                        /*strpsql*/"""
                            SELECT si_u."uuid", party.version_id
                            FROM register.self_identified_user AS si_u
                            INNER JOIN register.party AS party USING (uuid)
                            WHERE si_u.email = ANY (@selfIdentifiedEmails)
                            """);
                }

                Debug.Assert(!firstExpression);
                Debug.Assert(idSets.Count > 0, "No filters were added, but multiple filters were requested");

                _builder.AppendLine(",").Append(name).AppendLine(/*strpsql*/" AS (");
                for (int i = 0, l = idSets.Count; i < l; i++)
                {
                    if (i != 0)
                    {
                        _builder.AppendLine(/*strpsql*/"    UNION");
                    }

                    _builder.Append(/*strpsql*/"""    SELECT "uuid", version_id FROM """).AppendLine(idSets[i]);
                }

                _builder.Append(')');
            }

            private void PopulateListTransformCommonTableExpression(string name, string source, PartyListTransforms transforms, ref bool firstExpression)
            {
                // source input is expected to have the columns "uuid" and "version_id"
                // Output columns:
                // - uuid: the returned party UUID
                // - parent_uuid: the parent UUID for subunits; otherwise NULL
                // - sort_first: the party's own version ID, or its parent's version ID for an included subunit
                // - sort_second: the child UUID for subunits; otherwise NULL
                AddCommonTableExpression(
                    ref firstExpression,
                    "trans_input",
                    $"""
                        SELECT
                            "uuid" AS "uuid",
                            NULL::uuid AS parent_uuid,
                            version_id AS sort_first,
                            NULL::uuid AS sort_second
                        FROM {source}
                        """);

                source = "trans_input";

                if (transforms.HasFlag(PartyListTransforms.ReplaceWithMainUnits))
                {
                    AddCommonTableExpression(
                        ref firstExpression,
                        "main_unit_trans",
                        /*strpsql*/$"""
                        SELECT DISTINCT
                            party."uuid" AS "uuid",
                            NULL::uuid AS parent_uuid,
                            party.version_id AS sort_first,
                            NULL::uuid AS sort_second
                        FROM register.external_main_unit_role mur
                        JOIN register.external_role_assignment ra ON mur.source = ra.source AND mur.identifier = ra.identifier
                        JOIN register.party party ON ra.to_party = party."uuid"
                        JOIN {source} source ON ra.from_party = source."uuid"
                        """);

                    source = "main_unit_trans";
                }

                if (transforms.HasFlag(PartyListTransforms.IncludeSubUnits))
                {
                    // A given main-unit/sub-unit pair should have at most one main-unit role assignment.
                    // For example, sub-unit "Sub" should not point to main unit "Main" using both
                    // "hovedenhet" and "ikke-naeringsdrivende-hovedenhet".
                    // A sub-unit pointing to multiple distinct main units is also invalid, but is known
                    // to occur in source datasets and is intentionally preserved.
                    AddCommonTableExpression(
                        ref firstExpression,
                        "sub_units",
                        /*strpsql*/$"""
                        SELECT
                            source."uuid" AS parent_uuid,
                            source.sort_first AS sort_first,
                            ra."from_party" AS child_uuid
                        FROM {source} AS source
                        JOIN register.external_role_assignment ra ON ra.to_party = source."uuid"
                        JOIN register.external_main_unit_role mur ON mur.source = ra.source AND mur.identifier = ra.identifier
                        """);

                    AddCommonTableExpression(
                        ref firstExpression,
                        "sub_units_trans",
                        /*strpsql*/$"""
                        SELECT
                            "uuid" AS "uuid",
                            NULL::uuid AS parent_uuid,
                            sort_first AS sort_first,
                            NULL::uuid AS sort_second
                        FROM {source}

                        UNION ALL

                        SELECT
                            child_uuid AS "uuid",
                            parent_uuid,
                            sort_first,
                            child_uuid AS sort_second
                        FROM sub_units
                        """);

                    source = "sub_units_trans";
                }

                AddCommonTableExpression(
                    ref firstExpression,
                    name,
                    /*strpsql*/$"""
                    SELECT
                        "uuid",
                        parent_uuid,
                        sort_first,
                        sort_second
                    FROM {source}
                    """);
            }

            private void PopulateListFilterCommonTableExpression(string name, string? source, PartyListFilters filters, bool streamPage, ref bool firstExpression)
            {
                Debug.Assert(filters is not PartyListFilters.None || streamPage);
                if (streamPage)
                {
                    _paramStreamFromExclusive = new(typeof(long), "streamFromExclusive", NpgsqlDbType.Bigint);
                    _paramStreamLimit = new(typeof(int), "streamLimit", NpgsqlDbType.Integer);
                    AddCommonTableExpression(
                        ref firstExpression,
                        "maxval",
                        /*strpsql*/"""
                        SELECT register.tx_max_safeval('register.party_version_id_seq') maxval
                        """);
                }

                if (!firstExpression)
                {
                    _builder.AppendLine(",");
                }

                firstExpression = false;
                _builder.Append(name).AppendLine(/*strpsql*/" AS (");
                _builder.AppendLine(/*strpsql*/"    SELECT party.\"uuid\", party.version_id");
                if (source is null)
                {
                    _builder.AppendLine(/*strpsql*/"    FROM register.party AS party");
                }
                else
                {
                    _builder.Append(/*strpsql*/"    FROM ").Append(source).AppendLine(/*strpsql*/" AS source");
                    _builder.AppendLine(/*strpsql*/"    INNER JOIN register.party AS party USING (uuid)");
                }

                var whereAdded = false;
                if (streamPage)
                {
                    _builder.AppendLine(/*strpsql*/"    CROSS JOIN maxval mv");
                    _builder.AppendLine(/*strpsql*/"    WHERE party.version_id > @streamFromExclusive");
                    _builder.AppendLine(/*strpsql*/"      AND party.version_id <= mv.maxval");
                    whereAdded = true;
                }

                if (filters.HasFlag(PartyListFilters.PartyType))
                {
                    _paramPartyTypeList = new(typeof(IList<PartyRecordType>), "partyTypes", default);
                    var kw = whereAdded ? "  AND" : "WHERE";
                    whereAdded = true;

                    _builder.Append("    ").Append(kw).AppendLine(/*strpsql*/" party.party_type = ANY (@partyTypes)");
                }

                if (streamPage)
                {
                    _builder.AppendLine(/*strpsql*/"    ORDER BY party.version_id ASC");
                    _builder.AppendLine(/*strpsql*/"    LIMIT @streamLimit");
                }

                _builder.Append(')');
            }

            private void AddCommonTableExpression(ref bool firstExpression, string name, string query)
            {
                if (firstExpression)
                {
                    _builder.Append(/*strpsql*/"WITH ");
                    firstExpression = false;
                }
                else
                {
                    _builder.AppendLine(/*strpsql*/",");
                }

                _builder.Append(name).AppendLine(/*strpsql*/" AS (");
                foreach (var line in query.AsSpan().EnumerateLines())
                {
                    _builder.Append("    ").Append(line).AppendLine();
                }

                _builder.Append(')');
            }

            private sbyte AddField(string sourceSql, string fieldAlias, bool include)
            {
                if (!include)
                {
                    return -1;
                }

                if (_fieldIndex > 0)
                {
                    _builder.Append(',');
                }

                _builder.AppendLine();
                _builder.Append("    ").Append(sourceSql).Append(' ').Append(fieldAlias);

                return _fieldIndex++;
            }
        }

        private readonly struct FilterParameter(
            Type type,
            string name,
            NpgsqlDbType dbType)
        {
            [MemberNotNullWhen(true, nameof(Type))]
            [MemberNotNullWhen(true, nameof(Name))]
            public bool HasValue => Type is not null;

            public Type? Type => type;

            public string? Name => name;

            public NpgsqlDbType DbType => dbType;
        }

        [SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1515:Single-line comment should be preceded by blank line", Justification = "This rule makes no sense here")]
        private class PartyFields(
            // meta fields
            sbyte parentUuid,

            // register.party
            sbyte partyUuid,
            sbyte partyId,
            sbyte partyType,
            sbyte partyExternalUrn,
            sbyte partyDisplayName,
            sbyte partyPersonIdentifier,
            sbyte partyOrganizationIdentifier,
            sbyte partyCreated,
            sbyte partyUpdated,
            sbyte partyIsDeleted,
            sbyte partyDeletedAt,
            sbyte partyVersionId,
            sbyte partyOwnerUuid,

            // register.person
            sbyte personFirstName,
            sbyte personMiddleName,
            sbyte personLastName,
            sbyte personShortName,
            sbyte personDateOfBirth,
            sbyte personDateOfDeath,
            sbyte personAddress,
            sbyte personMailingAddress,
            sbyte personSource,

            // register.organization
            sbyte organizationUnitStatus,
            sbyte organizationUnitType,
            sbyte organizationTelephoneNumber,
            sbyte organizationMobileNumber,
            sbyte organizationFaxNumber,
            sbyte organizationEmailAddress,
            sbyte organizationInternetAddress,
            sbyte organizationMailingAddress,
            sbyte organizationBusinessAddress,
            sbyte organizationSource,

            // register.self_identified_user
            sbyte selfIdentifiedUserType,
            sbyte selfIdentifiedUserEmail,
            sbyte selfIdentifiedUserExtRef,

            // register.system_user
            sbyte systemUserType,

            // register.user
            sbyte activeUserId,
            sbyte userIds,

            // register.username
            sbyte activeUsername,
            sbyte usernames)
        {
            // meta field
            public int ParentUuid => parentUuid;

            // register.party
            public int PartyUuid => partyUuid;
            public int PartyId => partyId;
            public int PartyRecordType => partyType;
            public int PartyExternalUrn => partyExternalUrn;
            public int PartyDisplayName => partyDisplayName;
            public int PartyPersonIdentifier => partyPersonIdentifier;
            public int PartyOrganizationIdentifier => partyOrganizationIdentifier;
            public int PartyCreated => partyCreated;
            public int PartyUpdated => partyUpdated;
            public int PartyIsDeleted => partyIsDeleted;
            public int PartyDeletedAt => partyDeletedAt;
            public int PartyVersionId => partyVersionId;
            public int PartyOwnerUuid => partyOwnerUuid;

            // register.person
            public int PersonFirstName => personFirstName;
            public int PersonMiddleName => personMiddleName;
            public int PersonLastName => personLastName;
            public int PersonShortName => personShortName;
            public int PersonDateOfBirth => personDateOfBirth;
            public int PersonDateOfDeath => personDateOfDeath;
            public int PersonAddress => personAddress;
            public int PersonMailingAddress => personMailingAddress;
            public int PersonSource => personSource;

            // register.organization
            public int OrganizationUnitStatus => organizationUnitStatus;
            public int OrganizationUnitType => organizationUnitType;
            public int OrganizationTelephoneNumber => organizationTelephoneNumber;
            public int OrganizationMobileNumber => organizationMobileNumber;
            public int OrganizationFaxNumber => organizationFaxNumber;
            public int OrganizationEmailAddress => organizationEmailAddress;
            public int OrganizationInternetAddress => organizationInternetAddress;
            public int OrganizationMailingAddress => organizationMailingAddress;
            public int OrganizationBusinessAddress => organizationBusinessAddress;
            public int OrganizationSource => organizationSource;

            // register.self_identified_user
            public int SelfIdentifiedUserType => selfIdentifiedUserType;
            public int SelfIdentifiedUserEmail => selfIdentifiedUserEmail;
            public int SelfIdentifiedUserExtRef => selfIdentifiedUserExtRef;

            // register.system_user
            public int SystemUserType => systemUserType;

            // register.user
            public int ActiveUserId => activeUserId;
            public int UserIds => userIds;

            // register.username
            public int ActiveUsername => activeUsername;
            public int Usernames => usernames;
        }
    }
}
