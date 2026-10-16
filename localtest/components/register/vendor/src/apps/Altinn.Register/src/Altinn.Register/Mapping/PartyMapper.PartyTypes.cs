using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Mapping;

/// <summary>
/// Mappers for converting from <see cref="PartyRecord"/>
/// to <see cref="Party"/>.
/// </summary>
internal static partial class PartyMapper
{
    private static readonly string PARTY_TYPE_NAME = typeof(Party).FullName!;
    private static readonly string PERSON_TYPE_NAME = typeof(Person).FullName!;
    private static readonly string ORGANIZATION_TYPE_NAME = typeof(Organization).FullName!;
    private static readonly string SELF_IDENTIFIED_USER_TYPE_NAME = typeof(SelfIdentifiedUser).FullName!;
    private static readonly string ENTERPRISE_USER_TYPE_NAME = typeof(EnterpriseUser).FullName!;
    private static readonly string SYSTEM_USER_TYPE_NAME = typeof(SystemUser).FullName!;

    [return: NotNullIfNotNull(nameof(source))]
    private static Person? MapPerson(PersonRecord? source)
    {
        if (source is null)
        {
            return null;
        }

        if (!source.PartyUuid.HasValue)
        {
            ThrowHelper.ThrowInvalidOperationException($"{PARTY_TYPE_NAME} requires a {nameof(Party.Uuid)} to be set.");
        }

        if (!source.VersionId.HasValue)
        {
            ThrowHelper.ThrowInvalidOperationException($"{PARTY_TYPE_NAME} requires a {nameof(Party.VersionId)} to be set.");
        }

        if (!source.PersonIdentifier.HasValue)
        {
            ThrowHelper.ThrowInvalidOperationException($"{PERSON_TYPE_NAME} requires a {nameof(Person.PersonIdentifier)} to be set.");
        }

        Debug.Assert(source.PartyType.IsSet && source.PartyType.Value == PartyRecordType.Person);

        return new Person
        {
            Uuid = source.PartyUuid.Value,
            VersionId = source.VersionId.Value,
            PartyId = source.PartyId,
            ExternalUrn = source.ExternalUrn.Select(NonExhaustive.Create),
            DisplayName = source.DisplayName,
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt,
            IsDeleted = source.IsDeleted,
            DeletedAt = source.DeletedAt,
            User = MapUser(source.UserIds, source.Usernames),

            PersonIdentifier = source.PersonIdentifier.Value,
            Source = source.Source.Select(NonExhaustiveEnum.Create),
            FirstName = source.FirstName,
            MiddleName = source.MiddleName,
            LastName = source.LastName,
            ShortName = source.ShortName,
            Address = source.Address.Select(static a => a.ToPlatformModel()),
            MailingAddress = source.MailingAddress.Select(static a => a.ToPlatformModel()),
            DateOfBirth = source.DateOfBirth,
            DateOfDeath = source.DateOfDeath,
        };
    }

    [return: NotNullIfNotNull(nameof(source))]
    private static Organization? MapOrganization(OrganizationRecord? source)
    {
        if (source is null)
        {
            return null;
        }

        if (!source.PartyUuid.HasValue)
        {
            ThrowHelper.ThrowInvalidOperationException($"{PARTY_TYPE_NAME} requires a {nameof(Party.Uuid)} to be set.");
        }

        if (!source.VersionId.HasValue)
        {
            ThrowHelper.ThrowInvalidOperationException($"{PARTY_TYPE_NAME} requires a {nameof(Party.VersionId)} to be set.");
        }

        if (!source.OrganizationIdentifier.HasValue)
        {
            ThrowHelper.ThrowInvalidOperationException($"{ORGANIZATION_TYPE_NAME} requires an {nameof(Organization.OrganizationIdentifier)} to be set.");
        }

        Debug.Assert(source.PartyType.IsSet && source.PartyType.Value == PartyRecordType.Organization);

        return new Organization
        {
            Uuid = source.PartyUuid.Value,
            VersionId = source.VersionId.Value,
            PartyId = source.PartyId,
            ExternalUrn = source.ExternalUrn.Select(NonExhaustive.Create),
            DisplayName = source.DisplayName,
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt,
            IsDeleted = source.IsDeleted,
            DeletedAt = source.DeletedAt,
            User = MapUser(source.UserIds, source.Usernames),

            OrganizationIdentifier = source.OrganizationIdentifier.Value,
            Source = source.Source.Select(NonExhaustiveEnum.Create),
            UnitStatus = source.UnitStatus,
            UnitType = source.UnitType,
            TelephoneNumber = source.TelephoneNumber,
            MobileNumber = source.MobileNumber,
            FaxNumber = source.FaxNumber,
            EmailAddress = source.EmailAddress,
            InternetAddress = source.InternetAddress,
            MailingAddress = source.MailingAddress.Select(static a => a.ToPlatformModel()),
            BusinessAddress = source.BusinessAddress.Select(static a => a.ToPlatformModel()),
        };
    }

    [return: NotNullIfNotNull(nameof(source))]
    private static SelfIdentifiedUser? MapSelfIdentifiedUser(this SelfIdentifiedUserRecord? source)
    {
        if (source is null)
        {
            return null;
        }

        if (!source.PartyUuid.HasValue)
        {
            ThrowHelper.ThrowInvalidOperationException($"{PARTY_TYPE_NAME} requires a {nameof(Party.Uuid)} to be set.");
        }

        if (!source.VersionId.HasValue)
        {
            ThrowHelper.ThrowInvalidOperationException($"{PARTY_TYPE_NAME} requires a {nameof(Party.VersionId)} to be set.");
        }

        Debug.Assert(source.PartyType.IsSet && source.PartyType.Value == PartyRecordType.SelfIdentifiedUser);

        return new SelfIdentifiedUser
        {
            Uuid = source.PartyUuid.Value,
            VersionId = source.VersionId.Value,
            PartyId = source.PartyId,
            ExternalUrn = source.ExternalUrn.Select(NonExhaustive.Create),
            DisplayName = source.DisplayName,
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt,
            IsDeleted = source.IsDeleted,
            DeletedAt = source.DeletedAt,
            User = MapUser(source.UserIds, source.Usernames),
            SelfIdentifiedUserType = source.SelfIdentifiedUserType.Select(static t => NonExhaustiveEnum.Create(t)),
            Email = source.Email,
        };
    }

    [return: NotNullIfNotNull(nameof(source))]
    private static EnterpriseUser? MapEnterpriseUser(this EnterpriseUserRecord? source)
    {
        if (source is null)
        {
            return null;
        }

        if (!source.PartyUuid.HasValue)
        {
            ThrowHelper.ThrowInvalidOperationException($"{PARTY_TYPE_NAME} requires a {nameof(Party.Uuid)} to be set.");
        }

        if (!source.VersionId.HasValue)
        {
            ThrowHelper.ThrowInvalidOperationException($"{PARTY_TYPE_NAME} requires a {nameof(Party.VersionId)} to be set.");
        }

        if (source.OwnerUuid.IsNull)
        {
            ThrowHelper.ThrowInvalidOperationException($"{ENTERPRISE_USER_TYPE_NAME} requires {nameof(EnterpriseUser.Owner)} to not be null.");
        }

        Debug.Assert(source.PartyType.IsSet && source.PartyType.Value == PartyRecordType.EnterpriseUser);

        return new EnterpriseUser
        {
            Uuid = source.PartyUuid.Value,
            VersionId = source.VersionId.Value,
            PartyId = source.PartyId,
            ExternalUrn = source.ExternalUrn.Select(NonExhaustive.Create),
            DisplayName = source.DisplayName,
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt,
            IsDeleted = source.IsDeleted,
            DeletedAt = source.DeletedAt,
            User = MapUser(source.UserIds, source.Usernames),
            Owner = source.OwnerUuid.Select(static uuid => new PartyRef { Uuid = uuid }),
        };
    }

    [return: NotNullIfNotNull(nameof(source))]
    private static SystemUser? MapSystemUser(this SystemUserRecord? source)
    {
        if (source is null)
        {
            return null;
        }

        if (!source.PartyUuid.HasValue)
        {
            ThrowHelper.ThrowInvalidOperationException($"{PARTY_TYPE_NAME} requires a {nameof(Party.Uuid)} to be set.");
        }

        if (!source.VersionId.HasValue)
        {
            ThrowHelper.ThrowInvalidOperationException($"{PARTY_TYPE_NAME} requires a {nameof(Party.VersionId)} to be set.");
        }

        if (source.OwnerUuid.IsNull)
        {
            ThrowHelper.ThrowInvalidOperationException($"{SYSTEM_USER_TYPE_NAME} requires {nameof(SystemUser.Owner)} to not be null.");
        }

        if (source.SystemUserType.IsNull)
        {
            ThrowHelper.ThrowInvalidOperationException($"{SYSTEM_USER_TYPE_NAME} requires {nameof(SystemUser.SystemUserType)} to not be null.");
        }

        Debug.Assert(source.PartyType.IsSet && source.PartyType.Value == PartyRecordType.SystemUser);

        return new SystemUser
        {
            Uuid = source.PartyUuid.Value,
            VersionId = source.VersionId.Value,
            PartyId = source.PartyId,
            ExternalUrn = source.ExternalUrn.Select(NonExhaustive.Create),
            DisplayName = source.DisplayName,
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt,
            IsDeleted = source.IsDeleted,
            DeletedAt = source.DeletedAt,
            User = MapUser(source.UserIds, source.Usernames),
            Owner = source.OwnerUuid.Select(static uuid => new PartyRef { Uuid = uuid }),
            SystemUserType = source.SystemUserType.Select(static t => NonExhaustiveEnum.Create(t.MapSystemUserType())),
        };
    }

    private static SystemUserType MapSystemUserType(this SystemUserRecordType source)
        => source switch
        {
            SystemUserRecordType.Standard => SystemUserType.FirstPartySystemUser,
            SystemUserRecordType.Agent => SystemUserType.ClientPartySystemUser,
            _ => ThrowHelper.ThrowArgumentOutOfRangeException<SystemUserType>(nameof(source), source, "Invalid system user type."),
        };

    private static FieldValue<PartyUser> MapUser(FieldValue<PartyHistoricalAggregate<uint>> userIds, FieldValue<PartyHistoricalAggregate<string>> usernames)
    {
        var userId = userIds.CurrentValue;
        var username = usernames.CurrentValue;

        if (userId.IsUnset && username.IsUnset)
        {
            return FieldValue.Unset;
        }

        if (!userId.HasValue && !username.HasValue)
        {
            return FieldValue.Null;
        }

        return new PartyUser(
            userId: userId,
            username: username,
            userIds: userIds.Values,
            usernames: usernames.Values);
    }
}
