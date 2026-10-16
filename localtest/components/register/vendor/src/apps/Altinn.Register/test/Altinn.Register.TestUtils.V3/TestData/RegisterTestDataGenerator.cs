using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Urn;
using CommunityToolkit.Diagnostics;
using Npgsql;

namespace Altinn.Register.TestUtils.TestData;

/// <summary>
/// Generator for test data.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed partial class RegisterTestDataGenerator
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly NpgsqlDataSource _db;
    private readonly TimeProvider _timeProvider;
    private readonly SharedDeterministicRandom _random = new(1457248541);

    private UsedIdentifiers? _used;

    public RegisterTestDataGenerator(
        NpgsqlDataSource db,
        TimeProvider timeProvider)
    {
        Guard.IsNotNull(db);
        Guard.IsNotNull(timeProvider);

        _db = db;
        _timeProvider = timeProvider;
    }

    private ValueTask<T> WithIdentifiers<T>(
        Func<UsedIdentifiers, SharedDeterministicRandom, T> func,
        CancellationToken cancellationToken)
    {
        return WithIdentifiers(
            func,
            static (used, rng, fn) => fn(used, rng),
            cancellationToken);
    }

    private ValueTask<T> WithIdentifiers<T, TData>(
        TData data,
        Func<UsedIdentifiers, SharedDeterministicRandom, TData, T> func,
        CancellationToken cancellationToken)
    {
        var used = Volatile.Read(ref _used);
        if (used is not null)
        {
            return new(func(used, _random, data));
        }

        return new(WaitAndRun(this, data, func, cancellationToken));

        static async Task<T> WaitAndRun(RegisterTestDataGenerator self, TData data, Func<UsedIdentifiers, SharedDeterministicRandom, TData, T> func, CancellationToken cancellationToken)
        {
            UsedIdentifiers? used;

            await self._lock.WaitAsync(cancellationToken);
            try
            {
                used = Volatile.Read(ref self._used);
                if (used is null)
                {
                    used = await UsedIdentifiers.Fetch(self._db, cancellationToken);
                    Volatile.Write(ref self._used, used);
                }
            }
            finally
            {
                self._lock.Release();
            }

            return func(used, self._random, data);
        }
    }

    public ValueTask<OrganizationIdentifier> GetNewOrgNumber(CancellationToken cancellationToken = default)
        => WithIdentifiers(
            static (used, rng) => used.GetNewOrgNumber(rng),
            cancellationToken);

    public ValueTask<PersonIdentifier> GetNewPersonIdentifier(
        DateOnly? birthDate = null,
        bool? isDNumber = null,
        CancellationToken cancellationToken = default)
        => WithIdentifiers(
            (birthDate: birthDate ?? GetRandomBirthDate(), isDNumber: isDNumber ?? GetRandomBool(chance: 0.2)),
            static (used, rng, data) => used.GetNewPersonIdentifier(rng, data.birthDate, data.isDNumber),
            cancellationToken);

    public ValueTask<uint> GetNextPartyId(CancellationToken cancellationToken = default)
        => WithIdentifiers(
            static (used, rng) => used.GetNextPartyId(),
            cancellationToken);

    public ValueTask<IReadOnlyList<uint>> GetNextUserIds(int count = 1, CancellationToken cancellationToken = default)
        => WithIdentifiers(
            ValueTuple.Create(count),
            static (used, rng, data) => used.GetNextUserIds(data.Item1),
            cancellationToken);

    public DateOnly GetRandomBirthDate()
    {
        var min = new DateOnly(1940, 01, 01);
        var maxExl = new DateOnly(2024, 01, 01);
        var value = _random.Next(min.DayNumber, maxExl.DayNumber);

        return DateOnly.FromDayNumber(value);
    }

    public bool GetRandomBool(double chance)
        => _random.NextBool(chance);

    private OrganizationRecord GetOrgData(
        UsedIdentifiers used,
        FieldValue<Guid> uuid = default,
        FieldValue<uint> id = default,
        FieldValue<string> name = default,
        FieldValue<OrganizationIdentifier> identifier = default,
        FieldValue<DateTimeOffset> createdAt = default,
        FieldValue<DateTimeOffset> modifiedAt = default,
        FieldValue<bool> isDeleted = default,
        FieldValue<string> unitStatus = default,
        FieldValue<string> unitType = default,
        FieldValue<string> telephoneNumber = default,
        FieldValue<string> mobileNumber = default,
        FieldValue<string> faxNumber = default,
        FieldValue<string> emailAddress = default,
        FieldValue<string> internetAddress = default,
        FieldValue<MailingAddressRecord> mailingAddress = default,
        FieldValue<MailingAddressRecord> businessAddress = default)
    {
        if (!id.HasValue)
        {
            id = used.GetNextPartyId();
        }

        if (!identifier.HasValue)
        {
            identifier = used.GetNewOrgNumber(_random);
        }

        var now = _timeProvider.GetUtcNow();
        if (!createdAt.HasValue)
        {
            createdAt = now;
        }

        if (!modifiedAt.HasValue)
        {
            modifiedAt = now;
        }

        Debug.Assert(identifier.HasValue);
        return new OrganizationRecord
        {
            PartyUuid = uuid.HasValue ? uuid.Value : _random.Guid(),
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.OrganizationId.Create(identifier.Value),
            DisplayName = name.HasValue ? name.Value : "Test",
            PersonIdentifier = null,
            OrganizationIdentifier = identifier,
            CreatedAt = createdAt,
            ModifiedAt = modifiedAt,
            IsDeleted = isDeleted.HasValue ? isDeleted.Value : false,
            DeletedAt = isDeleted.HasValue && isDeleted.Value ? modifiedAt : FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = identifier.Value.ToString().StartsWith('0') ? OrganizationSource.RegisteredWithSkatteetaten : OrganizationSource.CentralCoordinatingRegister,
            UnitStatus = unitStatus.HasValue ? unitStatus.Value : "N",
            UnitType = unitType.HasValue ? unitType.Value : "AS",
            TelephoneNumber = telephoneNumber.IsSet ? telephoneNumber.Value : "99999999",
            MobileNumber = mobileNumber.IsSet ? mobileNumber.Value : "98888888",
            FaxNumber = faxNumber.IsSet ? faxNumber.Value : "97777777",
            EmailAddress = emailAddress.IsSet ? emailAddress.Value : $"{identifier.Value}@example.com",
            InternetAddress = internetAddress.IsSet ? internetAddress.Value : $"{identifier.Value}.example.com",
            MailingAddress = mailingAddress.IsSet ? mailingAddress.Value : new MailingAddressRecord { Address = identifier.Value.ToString(), City = "Mailing", PostalCode = "0000" },
            BusinessAddress = businessAddress.IsSet ? businessAddress.Value : new MailingAddressRecord { Address = identifier.Value.ToString(), City = "Business", PostalCode = "0000" },
        };
    }

    public ValueTask<OrganizationRecord> GetOrgData(
        FieldValue<Guid> uuid = default,
        FieldValue<uint> id = default,
        FieldValue<string> name = default,
        FieldValue<OrganizationIdentifier> identifier = default,
        FieldValue<DateTimeOffset> createdAt = default,
        FieldValue<DateTimeOffset> modifiedAt = default,
        FieldValue<bool> isDeleted = default,
        FieldValue<string> unitStatus = default,
        FieldValue<string> unitType = default,
        FieldValue<string> telephoneNumber = default,
        FieldValue<string> mobileNumber = default,
        FieldValue<string> faxNumber = default,
        FieldValue<string> emailAddress = default,
        FieldValue<string> internetAddress = default,
        FieldValue<MailingAddressRecord> mailingAddress = default,
        FieldValue<MailingAddressRecord> businessAddress = default,
        CancellationToken cancellationToken = default)
    {
        return WithIdentifiers(
            (self: this, uuid, id, name, identifier, createdAt, modifiedAt, isDeleted, unitStatus, unitType, telephoneNumber, mobileNumber, faxNumber, emailAddress, internetAddress, mailingAddress, businessAddress),
            static (used, rng, data)
                => data.self.GetOrgData(
                    used,
                    data.uuid,
                    data.id,
                    data.name,
                    data.identifier,
                    data.createdAt,
                    data.modifiedAt,
                    data.isDeleted,
                    data.unitStatus,
                    data.unitType,
                    data.telephoneNumber,
                    data.mobileNumber,
                    data.faxNumber,
                    data.emailAddress,
                    data.internetAddress,
                    data.mailingAddress,
                    data.businessAddress),
            cancellationToken);
    }

    private ImmutableArray<OrganizationRecord> GetOrgsData(
        UsedIdentifiers used,
        FieldValue<string> unitType,
        int count)
    {
        var builder = ImmutableArray.CreateBuilder<OrganizationRecord>(count);
        for (int i = 0; i < count; i++)
        {
            var org = GetOrgData(used, unitType: unitType);
            builder.Add(org);
        }

        return builder.DrainToImmutable();
    }

    public ValueTask<ImmutableArray<OrganizationRecord>> GetOrgsData(
        int count,
        FieldValue<string> unitType = default,
        CancellationToken cancellationToken = default)
    {
        return WithIdentifiers(
            (self: this, count, unitType),
            static (used, rng, data) => data.self.GetOrgsData(used, data.unitType, data.count),
            cancellationToken);
    }

    private PersonRecord GetPersonData(
        UsedIdentifiers used,
        FieldValue<Guid> uuid = default,
        FieldValue<uint> id = default,
        FieldValue<PersonIdentifier> identifier = default,
        FieldValue<DateTimeOffset> createdAt = default,
        FieldValue<DateTimeOffset> modifiedAt = default,
        FieldValue<PersonName> name = default,
        FieldValue<StreetAddressRecord> address = default,
        FieldValue<MailingAddressRecord> mailingAddress = default,
        FieldValue<DateOnly> dateOfBirth = default,
        FieldValue<DateOnly> dateOfDeath = default,
        FieldValue<PartyUserRecord> user = default)
    {
        if (!id.HasValue)
        {
            id = used.GetNextPartyId();
        }

        if (!dateOfBirth.IsSet)
        {
            // 10% chance of having no date of birth
            dateOfBirth = _random.NextBool(0.1)
                ? GetRandomBirthDate()
                : FieldValue.Null;
        }

        if (!identifier.HasValue)
        {
            var dateOfBirthValue = dateOfBirth.HasValue ? dateOfBirth.Value : GetRandomBirthDate();
            identifier = used.GetNewPersonIdentifier(_random, dateOfBirthValue, isDNumber: false);
        }

        if (!address.HasValue)
        {
            address = new StreetAddressRecord
            {
                MunicipalNumber = "0001",
                MunicipalName = "Test",
                StreetName = "Testveien",
                HouseNumber = "1",
                HouseLetter = null,
                PostalCode = "0001",
                City = "Testby",
            };
        }

        if (!mailingAddress.HasValue)
        {
            mailingAddress = new MailingAddressRecord
            {
                Address = $"{address.Value!.StreetName} {address.Value.HouseNumber}",
                PostalCode = address.Value.PostalCode,
                City = address.Value.City,
            };
        }

        if (!name.HasValue)
        {
            name = PersonName.Create("Test", "Mid", "Testson");
        }

        if (dateOfDeath.IsUnset)
        {
            dateOfDeath = FieldValue.Null;
        }

        if (user.IsUnset)
        {
            var userIdsEnumerable = used.GetNextUserIds(3);
            var userIds = userIdsEnumerable.Select(static id => (uint)id).OrderByDescending(static id => id).ToImmutableValueArray();
            var userId = userIds[0];

            user = new PartyUserRecord(userId: userId, username: FieldValue.Unset, userIds: userIds);
        }
        else if (user.IsNull)
        {
            user = FieldValue.Unset;
        }

        var now = _timeProvider.GetUtcNow();
        if (!createdAt.HasValue)
        {
            createdAt = now;
        }

        if (!modifiedAt.HasValue)
        {
            modifiedAt = now;
        }

        Debug.Assert(identifier.HasValue);
        return new PersonRecord
        {
            PartyUuid = uuid.HasValue ? uuid.Value : Guid.NewGuid(),
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(identifier.Value),
            DisplayName = name.Value!.DisplayName,
            PersonIdentifier = identifier,
            OrganizationIdentifier = null,
            CreatedAt = createdAt,
            ModifiedAt = modifiedAt,
            IsDeleted = dateOfDeath.HasValue,
            DeletedAt = dateOfDeath.HasValue ? new DateTimeOffset(dateOfDeath.Value, TimeOnly.MinValue, TimeSpan.Zero) : FieldValue.Null,
            UserIds = user.SelectFieldValue(static u => u.UserIds) switch
            {
                { IsUnset: true } => FieldValue.Unset,
                { IsNull: true } => PartyHistoricalAggregate<uint>.Empty,
                var ids => PartyHistoricalAggregate<uint>.Create(ids.Value, hasActiveValue: !ids.Value.IsEmpty),
            },
            Usernames = user.SelectFieldValue(static u => u.Username) switch
            {
                { IsUnset: true } => FieldValue.Unset,
                { IsNull: true } => PartyHistoricalAggregate<string>.Empty,
                var username => PartyHistoricalAggregate<string>.Create([username.Value!], hasActiveValue: true),
            },
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = name.Value.FirstName,
            MiddleName = name.Value.MiddleName,
            LastName = name.Value.LastName,
            ShortName = name.Value.ShortName,
            Address = address,
            MailingAddress = mailingAddress,
            DateOfBirth = dateOfBirth,
            DateOfDeath = dateOfDeath,
        };
    }

    public ValueTask<PersonRecord> GetPersonData(
        FieldValue<Guid> uuid = default,
        FieldValue<uint> id = default,
        FieldValue<PersonIdentifier> identifier = default,
        FieldValue<DateTimeOffset> createdAt = default,
        FieldValue<DateTimeOffset> modifiedAt = default,
        FieldValue<PersonName> name = default,
        FieldValue<StreetAddressRecord> address = default,
        FieldValue<MailingAddressRecord> mailingAddress = default,
        FieldValue<DateOnly> dateOfBirth = default,
        FieldValue<DateOnly> dateOfDeath = default,
        FieldValue<PartyUserRecord> user = default,
        CancellationToken cancellationToken = default)
    {
        return WithIdentifiers(
            (self: this, uuid, id, identifier, createdAt, modifiedAt, name, address, mailingAddress, dateOfBirth, dateOfDeath, user),
            static (used, rng, data)
                => data.self.GetPersonData(
                    used,
                    data.uuid,
                    data.id,
                    data.identifier,
                    data.createdAt,
                    data.modifiedAt,
                    data.name,
                    data.address,
                    data.mailingAddress,
                    data.dateOfBirth,
                    data.dateOfDeath,
                    data.user),
            cancellationToken);
    }

    private ImmutableArray<PersonRecord> GetPeopleData(
        UsedIdentifiers used,
        int count)
    {
        var builder = ImmutableArray.CreateBuilder<PersonRecord>(count);
        for (int i = 0; i < count; i++)
        {
            var person = GetPersonData(used);
            builder.Add(person);
        }

        return builder.DrainToImmutable();
    }

    public ValueTask<ImmutableArray<PersonRecord>> GetPeopleData(int count, CancellationToken cancellationToken = default)
    {
        return WithIdentifiers(
            (self: this, count),
            static (used, rng, data) => data.self.GetPeopleData(used, data.count),
            cancellationToken);
    }

    private SelfIdentifiedUserRecord GetSelfIdentifiedUserData(
        UsedIdentifiers used,
        FieldValue<Guid> uuid = default,
        FieldValue<uint> id = default,
        FieldValue<string> name = default,
        FieldValue<DateTimeOffset> createdAt = default,
        FieldValue<DateTimeOffset> modifiedAt = default,
        FieldValue<bool> isDeleted = default,
        FieldValue<SelfIdentifiedUserType> type = default,
        FieldValue<string> email = default,
        FieldValue<string> extRef = default,
        FieldValue<PartyUserRecord> user = default)
    {
        if (id.IsUnset)
        {
            id = used.GetNextPartyId();
        }

        if (type.IsUnset)
        {
            switch (email, extRef)
            {
                case ({ HasValue: true }, { HasValue: true }):
                    ThrowHelper.ThrowInvalidOperationException("Cannot specify both email and extRef for a self-identified user.");
                    break;

                case ({ HasValue: true }, _):
                    type = SelfIdentifiedUserType.IdPortenEmail;
                    break;

                case (_, { HasValue: true }):
                    type = SelfIdentifiedUserType.Educational;
                    break;

                default:
                    List<SelfIdentifiedUserType> possible = Enum.GetValues<SelfIdentifiedUserType>().ToList();

                    if (email.IsNull)
                    {
                        possible.Remove(SelfIdentifiedUserType.IdPortenEmail);
                    }

                    if (extRef.IsNull)
                    {
                        possible.Remove(SelfIdentifiedUserType.Educational);
                    }

                    type = _random.Next(possible);
                    break;
            }
        }

        FieldValue<PartyExternalRefUrn> externalUrn = FieldValue.Null;
        if (type.HasValue)
        {
            switch (type.Value)
            {
                case SelfIdentifiedUserType.Legacy:
                    if (name.IsUnset)
                    {
                        name = $"si-user-{id.Value}";
                    }

                    externalUrn = name.Select(static n => (PartyExternalRefUrn)PartyExternalRefUrn.LegacySelfIdentifiedUsername.Create(UrnEncoded.Create(n)));
                    break;

                case SelfIdentifiedUserType.IdPortenEmail:
                    if (email.IsUnset)
                    {
                        email = $"email-{id.Value}@example.com";
                    }

                    if (name.IsUnset)
                    {
                        name = $"email:{email.Value}";
                    }

                    externalUrn = email.Select(static e => (PartyExternalRefUrn)PartyExternalRefUrn.IDPortenEmail.Create(UrnEncoded.Create(e)));
                    break;

                case SelfIdentifiedUserType.Educational:
                    if (extRef.IsUnset)
                    {
                        extRef = $"extref-{id.Value}";
                    }

                    if (name.IsUnset)
                    {
                        name = $"si-edu-user-{id.Value}";
                    }

                    break;
            }

            if (!email.IsSet)
            {
                email = FieldValue.Null;
            }

            if (!extRef.IsSet)
            {
                extRef = FieldValue.Null;
            }
        }

        if (user.IsUnset)
        {
            var userIdsEnumerable = used.GetNextUserIds(3);
            var userIds = userIdsEnumerable.Select(static id => (uint)id).OrderByDescending(static id => id).ToImmutableValueArray();
            var userId = userIds[0];

            var username = type is { IsNull: true } or { Value: SelfIdentifiedUserType.Legacy }
                ? name
                : FieldValue.Null;

            user = new PartyUserRecord(userId: userId, username: username, userIds: userIds);
        }

        var now = _timeProvider.GetUtcNow();
        if (!createdAt.HasValue)
        {
            createdAt = now;
        }

        if (!modifiedAt.HasValue)
        {
            modifiedAt = now;
        }

        return new SelfIdentifiedUserRecord
        {
            PartyUuid = uuid.HasValue ? uuid.Value : Guid.NewGuid(),
            PartyId = id,
            ExternalUrn = externalUrn,
            DisplayName = name,
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = createdAt,
            ModifiedAt = modifiedAt,
            UserIds = user.SelectFieldValue(static u => u.UserIds) switch
            {
                { IsUnset: true } => FieldValue.Unset,
                { IsNull: true } => PartyHistoricalAggregate<uint>.Empty,
                var ids => PartyHistoricalAggregate<uint>.Create(ids.Value, hasActiveValue: !ids.Value.IsEmpty),
            },
            Usernames = user.SelectFieldValue(static u => u.Username) switch
            {
                { IsUnset: true } => FieldValue.Unset,
                { IsNull: true } => PartyHistoricalAggregate<string>.Empty,
                var username => PartyHistoricalAggregate<string>.Create([username.Value!], hasActiveValue: true),
            },
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            IsDeleted = isDeleted.OrDefault(defaultValue: false),
            DeletedAt = isDeleted.HasValue && isDeleted.Value ? modifiedAt : FieldValue.Null,
            SelfIdentifiedUserType = type,
            Email = email,
            ExtRef = extRef,
        };
    }

    public ValueTask<SelfIdentifiedUserRecord> GetSelfIdentifiedUserData(
        FieldValue<Guid> uuid = default,
        FieldValue<uint> id = default,
        FieldValue<string> name = default,
        FieldValue<DateTimeOffset> createdAt = default,
        FieldValue<DateTimeOffset> modifiedAt = default,
        FieldValue<bool> isDeleted = default,
        FieldValue<SelfIdentifiedUserType> type = default,
        FieldValue<string> email = default,
        FieldValue<string> extRef = default,
        FieldValue<PartyUserRecord> user = default,
        CancellationToken cancellationToken = default)
    {
        return WithIdentifiers(
            (self: this, uuid, id, name, createdAt, modifiedAt, isDeleted, type, email, extRef, user),
            static (used, rng, data)
                => data.self.GetSelfIdentifiedUserData(
                    used,
                    data.uuid,
                    data.id,
                    data.name,
                    data.createdAt,
                    data.modifiedAt,
                    data.isDeleted,
                    data.type,
                    data.email,
                    data.extRef,
                    data.user),
            cancellationToken);
    }

    private ImmutableArray<SelfIdentifiedUserRecord> GetSelfIdentifiedUsersData(
        UsedIdentifiers used,
        int count)
    {
        var builder = ImmutableArray.CreateBuilder<SelfIdentifiedUserRecord>(count);
        for (int i = 0; i < count; i++)
        {
            var user = GetSelfIdentifiedUserData(used);
            builder.Add(user);
        }

        return builder.DrainToImmutable();
    }

    public ValueTask<ImmutableArray<SelfIdentifiedUserRecord>> GetSelfIdentifiedUsersData(
        int count,
        CancellationToken cancellationToken = default)
    {
        return WithIdentifiers(
            (self: this, count),
            static (used, rng, data) => data.self.GetSelfIdentifiedUsersData(used, data.count),
            cancellationToken);
    }

    private EnterpriseUserRecord GetEnterpriseUserData(
        UsedIdentifiers used,
        Guid owner,
        FieldValue<Guid> uuid = default,
        FieldValue<string> name = default,
        FieldValue<DateTimeOffset> createdAt = default,
        FieldValue<DateTimeOffset> modifiedAt = default,
        FieldValue<bool> isDeleted = default,
        FieldValue<PartyUserRecord> user = default)
    {
        if (!uuid.HasValue)
        {
            uuid = Guid.NewGuid();
        }

        if (user.IsUnset)
        {
            var userIdsEnumerable = used.GetNextUserIds(1);
            var userIds = userIdsEnumerable.Select(static id => (uint)id).OrderByDescending(static id => id).ToImmutableValueArray();
            var userId = userIds[0];

            user = new PartyUserRecord(userId: userId, username: FieldValue.Unset, userIds: userIds);
        }

        if (name.IsUnset)
        {
            name = $"enterprise-user-{uuid.HasValue}";
        }

        var now = _timeProvider.GetUtcNow();
        if (!createdAt.HasValue)
        {
            createdAt = now;
        }

        if (!modifiedAt.HasValue)
        {
            modifiedAt = now;
        }

        return new EnterpriseUserRecord
        {
            PartyUuid = uuid.Value,
            PartyId = FieldValue.Null,
            ExternalUrn = FieldValue.Null,
            DisplayName = name,
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = createdAt,
            ModifiedAt = modifiedAt,
            UserIds = user.SelectFieldValue(static u => u.UserIds) switch
            {
                { IsUnset: true } => FieldValue.Unset,
                { IsNull: true } => PartyHistoricalAggregate<uint>.Empty,
                var ids => PartyHistoricalAggregate<uint>.Create(ids.Value, hasActiveValue: !ids.Value.IsEmpty),
            },
            Usernames = user.SelectFieldValue(static u => u.Username) switch
            {
                { IsUnset: true } => FieldValue.Unset,
                { IsNull: true } => PartyHistoricalAggregate<string>.Empty,
                var username => PartyHistoricalAggregate<string>.Create([username.Value!], hasActiveValue: true),
            },
            VersionId = FieldValue.Unset,
            OwnerUuid = owner,
            IsDeleted = isDeleted.OrDefault(defaultValue: false),
            DeletedAt = isDeleted.HasValue && isDeleted.Value ? modifiedAt : FieldValue.Null,
        };
    }

    public ValueTask<EnterpriseUserRecord> GetEnterpriseUserData(
        Guid owner,
        FieldValue<Guid> uuid = default,
        FieldValue<string> name = default,
        FieldValue<DateTimeOffset> createdAt = default,
        FieldValue<DateTimeOffset> modifiedAt = default,
        FieldValue<bool> isDeleted = default,
        FieldValue<PartyUserRecord> user = default,
        CancellationToken cancellationToken = default)
    {
        return WithIdentifiers(
            (self: this, owner, uuid, name, createdAt, modifiedAt, isDeleted, user),
            static (used, rng, data)
                => data.self.GetEnterpriseUserData(
                    used,
                    data.owner,
                    data.uuid,
                    data.name,
                    data.createdAt,
                    data.modifiedAt,
                    data.isDeleted,
                    data.user),
            cancellationToken);
    }

    private ImmutableArray<EnterpriseUserRecord> GetEnterpriseUsersData(
        UsedIdentifiers used,
        Guid owner,
        int count)
    {
        var builder = ImmutableArray.CreateBuilder<EnterpriseUserRecord>(count);
        for (int i = 0; i < count; i++)
        {
            var user = GetEnterpriseUserData(used, owner);
            builder.Add(user);
        }

        return builder.DrainToImmutable();
    }

    public ValueTask<ImmutableArray<EnterpriseUserRecord>> GetEnterpriseUsersData(
        Guid owner,
        int count,
        CancellationToken cancellationToken = default)
    {
        return WithIdentifiers(
            (self: this, owner, count),
            static (used, rng, data) => data.self.GetEnterpriseUsersData(used, data.owner, data.count),
            cancellationToken);
    }

    private SystemUserRecord GetSystemUserData(
        UsedIdentifiers used,
        Guid owner,
        FieldValue<Guid> uuid = default,
        FieldValue<string> name = default,
        FieldValue<DateTimeOffset> createdAt = default,
        FieldValue<DateTimeOffset> modifiedAt = default,
        FieldValue<bool> isDeleted = default,
        FieldValue<SystemUserRecordType> type = default)
    {
        if (!uuid.HasValue)
        {
            uuid = Guid.NewGuid();
        }

        if (type.IsUnset)
        {
            ReadOnlySpan<SystemUserRecordType> types = [SystemUserRecordType.Standard, SystemUserRecordType.Agent];
            type = types[Random.Shared.Next(types.Length)];
        }

        if (name.IsUnset)
        {
            name = $"system-user-{uuid.Value}";
        }

        var now = _timeProvider.GetUtcNow();
        if (!createdAt.HasValue)
        {
            createdAt = now;
        }

        if (!modifiedAt.HasValue)
        {
            modifiedAt = now;
        }

        Debug.Assert(uuid.HasValue);
        return new SystemUserRecord
        {
            PartyUuid = uuid.Value,
            PartyId = FieldValue.Null,
            ExternalUrn = PartyExternalRefUrn.SystemUserUuid.Create(uuid.Value),
            DisplayName = name,
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = createdAt,
            ModifiedAt = modifiedAt,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = owner,
            IsDeleted = isDeleted.OrDefault(defaultValue: false),
            DeletedAt = isDeleted.HasValue && isDeleted.Value ? modifiedAt : FieldValue.Null,
            SystemUserType = type,
        };
    }

    public ValueTask<SystemUserRecord> GetSystemUserData(
        Guid owner,
        FieldValue<Guid> uuid = default,
        FieldValue<string> name = default,
        FieldValue<DateTimeOffset> createdAt = default,
        FieldValue<DateTimeOffset> modifiedAt = default,
        FieldValue<bool> isDeleted = default,
        CancellationToken cancellationToken = default)
    {
        return WithIdentifiers(
            (self: this, owner, uuid, name, createdAt, modifiedAt, isDeleted),
            static (used, rng, data)
                => data.self.GetSystemUserData(
                    used,
                    data.owner,
                    data.uuid,
                    data.name,
                    data.createdAt,
                    data.modifiedAt,
                    data.isDeleted),
            cancellationToken);
    }

    private ImmutableArray<SystemUserRecord> GetSystemUsersData(
        UsedIdentifiers used,
        Guid owner,
        int count)
    {
        var builder = ImmutableArray.CreateBuilder<SystemUserRecord>(count);
        for (int i = 0; i < count; i++)
        {
            var user = GetSystemUserData(used, owner);
            builder.Add(user);
        }

        return builder.DrainToImmutable();
    }

    public ValueTask<ImmutableArray<SystemUserRecord>> GetSystemUsersData(
        Guid owner,
        int count,
        CancellationToken cancellationToken = default)
    {
        return WithIdentifiers(
            (self: this, owner, count),
            static (used, rng, data) => data.self.GetSystemUsersData(used, data.owner, data.count),
            cancellationToken);
    }
}
