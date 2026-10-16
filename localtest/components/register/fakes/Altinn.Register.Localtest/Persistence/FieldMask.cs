using Altinn.Authorization.ModelUtils;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Localtest.Persistence;

/// <summary>
/// Masks record fields that were not requested to <see cref="FieldValue.Unset"/>, the way the production
/// queries only select the requested columns.
/// </summary>
internal static class FieldMask
{
    public static PartyRecord Mask(PartyRecord record, PartyFieldIncludes include)
    {
        var masked = record switch
        {
            PersonRecord p => p with
            {
                Source = Keep(p.Source, include, PartyFieldIncludes.PersonSource),
                FirstName = Keep(p.FirstName, include, PartyFieldIncludes.PersonFirstName),
                MiddleName = Keep(p.MiddleName, include, PartyFieldIncludes.PersonMiddleName),
                LastName = Keep(p.LastName, include, PartyFieldIncludes.PersonLastName),
                ShortName = Keep(p.ShortName, include, PartyFieldIncludes.PersonShortName),
                Address = Keep(p.Address, include, PartyFieldIncludes.PersonAddress),
                MailingAddress = Keep(p.MailingAddress, include, PartyFieldIncludes.PersonMailingAddress),
                DateOfBirth = Keep(p.DateOfBirth, include, PartyFieldIncludes.PersonDateOfBirth),
                DateOfDeath = Keep(p.DateOfDeath, include, PartyFieldIncludes.PersonDateOfDeath),
            },
            OrganizationRecord o => o with
            {
                Source = Keep(o.Source, include, PartyFieldIncludes.OrganizationSource),
                UnitStatus = Keep(o.UnitStatus, include, PartyFieldIncludes.OrganizationUnitStatus),
                UnitType = Keep(o.UnitType, include, PartyFieldIncludes.OrganizationUnitType),
                TelephoneNumber = Keep(o.TelephoneNumber, include, PartyFieldIncludes.OrganizationTelephoneNumber),
                MobileNumber = Keep(o.MobileNumber, include, PartyFieldIncludes.OrganizationMobileNumber),
                FaxNumber = Keep(o.FaxNumber, include, PartyFieldIncludes.OrganizationFaxNumber),
                EmailAddress = Keep(o.EmailAddress, include, PartyFieldIncludes.OrganizationEmailAddress),
                InternetAddress = Keep(o.InternetAddress, include, PartyFieldIncludes.OrganizationInternetAddress),
                MailingAddress = Keep(o.MailingAddress, include, PartyFieldIncludes.OrganizationMailingAddress),
                BusinessAddress = Keep(o.BusinessAddress, include, PartyFieldIncludes.OrganizationBusinessAddress),
            },
            SystemUserRecord s => s with
            {
                SystemUserType = Keep(s.SystemUserType, include, PartyFieldIncludes.SystemUserType),
            },
            SelfIdentifiedUserRecord si => si with
            {
                SelfIdentifiedUserType = Keep(si.SelfIdentifiedUserType, include, PartyFieldIncludes.SelfIdentifiedUserType),
                Email = Keep(si.Email, include, PartyFieldIncludes.SelfIdentifiedUserEmail),
                ExtRef = Keep(si.ExtRef, include, PartyFieldIncludes.SelfIdentifiedUserExtRef),
            },
            _ => record,
        };

        return masked with
        {
            PartyUuid = Keep(masked.PartyUuid, include, PartyFieldIncludes.PartyUuid),
            PartyId = Keep(masked.PartyId, include, PartyFieldIncludes.PartyId),
            ExternalUrn = Keep(masked.ExternalUrn, include, PartyFieldIncludes.PartyExternalUrn),
            DisplayName = Keep(masked.DisplayName, include, PartyFieldIncludes.PartyDisplayName),
            PersonIdentifier = Keep(masked.PersonIdentifier, include, PartyFieldIncludes.PartyPersonIdentifier),
            OrganizationIdentifier = Keep(masked.OrganizationIdentifier, include, PartyFieldIncludes.PartyOrganizationIdentifier),
            CreatedAt = Keep(masked.CreatedAt, include, PartyFieldIncludes.PartyCreatedAt),
            ModifiedAt = Keep(masked.ModifiedAt, include, PartyFieldIncludes.PartyModifiedAt),
            IsDeleted = Keep(masked.IsDeleted, include, PartyFieldIncludes.PartyIsDeleted),
            DeletedAt = Keep(masked.DeletedAt, include, PartyFieldIncludes.PartyDeletedAt),
            VersionId = Keep(masked.VersionId, include, PartyFieldIncludes.PartyVersionId),
            OwnerUuid = Keep(masked.OwnerUuid, include, PartyFieldIncludes.PartyOwnerUuid),
            UserIds = Keep(masked.UserIds, include, PartyFieldIncludes.UserId),
            Usernames = Keep(masked.Usernames, include, PartyFieldIncludes.Username),
        };
    }

    private static FieldValue<T> Keep<T>(FieldValue<T> value, PartyFieldIncludes include, PartyFieldIncludes flag)
        where T : notnull
        => include.HasFlag(flag) ? value : FieldValue.Unset;
}
