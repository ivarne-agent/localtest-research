using System.Diagnostics;
using System.Runtime.CompilerServices;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;
using V1Models = Altinn.Register.Contracts.V1;

namespace Altinn.Register.Core.Operations;

/// <summary>
/// Maps party records to v1 contract models.
/// </summary>
internal static class V1PartyMapper
{
    /// <summary>
    /// Maps a <see cref="PartyRecord"/> to a <see cref="V1Models.Party"/>.
    /// </summary>
    /// <param name="party">The party record.</param>
    /// <returns>The mapped v1 party.</returns>
    internal static V1Models.Party ToV1Party(PartyRecord party)
    {
        Debug.Assert(party.DisplayName.HasValue);

        var ret = new V1Models.Party
        {
            PartyUuid = party.PartyUuid.Value,
            PartyId = checked((int)party.PartyId.Value),
            ExternalUrn = party.ExternalUrn.Value,
            Name = party.DisplayName.Value,
            IsDeleted = party.IsDeleted.Value,
            OnlyHierarchyElementWithNoAccess = false,
        };

        switch (party)
        {
            case OrganizationRecord org:
                var organization = ToV1Organization(org);
                ret.PartyTypeName = V1Models.PartyType.Organisation;
                ret.OrgNumber = organization.OrgNumber;
                ret.Name = organization.Name;
                ret.UnitType = organization.UnitType;
                ret.Organization = organization;
                break;

            case PersonRecord person:
                var mappedPerson = ToV1Person(person);
                ret.PartyTypeName = V1Models.PartyType.Person;
                ret.SSN = mappedPerson.SSN;
                ret.Name = mappedPerson.Name;
                ret.Person = mappedPerson;
                break;

            case SelfIdentifiedUserRecord siUser:
                ret.PartyTypeName = V1Models.PartyType.SelfIdentified;

                ret.Name = siUser.SelfIdentifiedUserType switch
                {
                    { HasValue: true, Value: SelfIdentifiedUserType.IdPortenEmail }
                        => siUser.Usernames.CurrentValue
                            .Or(siUser.Email.Select(static e => $"epost:{e}"))
                            .OrDefault(siUser.DisplayName.Value)!,
                    _ => siUser.Usernames.CurrentValue
                            .OrDefault(siUser.DisplayName.Value)!,
                };
                break;

            default:
                throw new UnreachableException($"Unsupported party type: {party.GetType().Name}");
        }

        return ret;
    }

    /// <summary>
    /// Maps a <see cref="PartyRecord"/> to a <see cref="V1Models.Party"/>.
    /// </summary>
    /// <param name="org">The organization record.</param>
    /// <param name="childOrgs">The child organization records.</param>
    /// <returns>The mapped v1 party.</returns>
    internal static V1Models.Party ToV1Party(OrganizationRecord org, IEnumerable<OrganizationRecord> childOrgs)
    {
        var ret = ToV1Party(org);
        ret.ChildParties = [.. childOrgs.Select(ToV1Party)];

        return ret;
    }

    /// <summary>
    /// Maps an <see cref="OrganizationRecord"/> to a <see cref="V1Models.Organization"/>.
    /// </summary>
    /// <param name="org">The organization record.</param>
    /// <returns>The mapped v1 organization.</returns>
    internal static V1Models.Organization ToV1Organization(OrganizationRecord org)
    {
        Debug.Assert(org.OrganizationIdentifier.HasValue);
        Debug.Assert(org.DisplayName.HasValue);

        var ret = new V1Models.Organization
        {
            OrgNumber = org.OrganizationIdentifier.Value.ToString(),
            Name = org.DisplayName.Value,
            UnitType = org.UnitType.Value,
            TelephoneNumber = org.TelephoneNumber.Value,
            MobileNumber = org.MobileNumber.Value,
            FaxNumber = org.FaxNumber.Value,
            EMailAddress = org.EmailAddress.Value,
            InternetAddress = org.InternetAddress.Value,
            UnitStatus = org.UnitStatus.Value,
        };

        if (org.MailingAddress.HasValue)
        {
            var mailingAddress = org.MailingAddress.Value;
            ret.MailingAddress = mailingAddress.Address;
            ret.MailingPostalCode = mailingAddress.PostalCode;
            ret.MailingPostalCity = mailingAddress.City;
        }

        if (org.BusinessAddress.HasValue)
        {
            var businessAddress = org.BusinessAddress.Value;
            ret.BusinessAddress = businessAddress.Address;
            ret.BusinessPostalCode = businessAddress.PostalCode;
            ret.BusinessPostalCity = businessAddress.City;
        }

        return ret;
    }

    /// <summary>
    /// Maps a <see cref="PersonRecord"/> to a <see cref="V1Models.Person"/>.
    /// </summary>
    /// <param name="person">The person record.</param>
    /// <returns>The mapped v1 person.</returns>
    internal static V1Models.Person ToV1Person(PersonRecord person)
    {
        Debug.Assert(person.PersonIdentifier.HasValue);
        Debug.Assert(person.DisplayName.HasValue);

        var ret = new V1Models.Person
        {
            SSN = person.PersonIdentifier.Value.ToString(),
            Name = person.ShortName.HasValue ? person.ShortName.Value : person.DisplayName.Value,
            FirstName = person.FirstName.Value,
            MiddleName = person.MiddleName.Value,
            LastName = person.LastName.Value,
        };

        if (person.MailingAddress.HasValue)
        {
            ret.MailingAddress = person.MailingAddress.Value.Address;
            ret.MailingPostalCode = person.MailingAddress.Value.PostalCode;
            ret.MailingPostalCity = person.MailingAddress.Value.City;
        }

        if (person.Address.HasValue)
        {
            ret.AddressMunicipalNumber = person.Address.Value.MunicipalNumber;
            ret.AddressMunicipalName = person.Address.Value.MunicipalName;
            ret.AddressStreetName = person.Address.Value.StreetName;
            ret.AddressHouseNumber = person.Address.Value.HouseNumber;
            ret.AddressHouseLetter = person.Address.Value.HouseLetter;
            ret.AddressPostalCode = person.Address.Value.PostalCode;
            ret.AddressCity = person.Address.Value.City;
        }

        if (person.DateOfDeath.HasValue)
        {
            ret.DateOfDeath = person.DateOfDeath.Value.ToDateTime(TimeOnly.MinValue);
        }

        return ret;
    }

    /// <summary>
    /// Maps a sequence of <see cref="PartyRecord"/> items to a v1 party list.
    /// </summary>
    /// <param name="parties">The party records to map.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The mapped v1 party list.</returns>
    /// <remarks>
    /// This method requires a strict ordering where a parent organization appears before any of its child units,
    /// and each child unit follows its parent immediately. This ordering contract is fulfilled by
    /// <see cref="Parties.IPartyPersistence.LookupParties(IReadOnlyList{Guid}?, IReadOnlyList{uint}?, IReadOnlyList{Altinn.Register.Contracts.PartyExternalRefUrn}?, IReadOnlyList{Altinn.Register.Contracts.OrganizationIdentifier}?, IReadOnlyList{Altinn.Register.Contracts.PersonIdentifier}?, IReadOnlyList{uint}?, IReadOnlyList{string}?, IReadOnlyList{string}?, Parties.PartyFieldIncludes, Parties.PartyListTransforms, CancellationToken)"/>.
    /// </remarks>
    internal static async IAsyncEnumerable<V1Models.Party> ToV1PartyList(
        IAsyncEnumerable<PartyRecord> parties,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        V1Models.Party? currentParent = null;
        List<V1Models.Party>? children = null;

        await foreach (var party in parties.WithCancellation(cancellationToken))
        {
            if (party is not OrganizationRecord org)
            {
                if (currentParent is not null)
                {
                    yield return currentParent;
                    currentParent = null;
                    children = null;
                }

                yield return ToV1Party(party);
                continue;
            }

            if (org.ParentOrganizationUuid.HasValue)
            {
                Debug.Assert(currentParent is not null);
                Debug.Assert(children is not null);
                Debug.Assert(currentParent.PartyUuid == org.ParentOrganizationUuid.Value);
                children.Add(ToV1Party(org));
                continue;
            }

            if (currentParent is not null)
            {
                yield return currentParent;
                currentParent = null;
                children = null;
            }

            currentParent = ToV1Party(org);
            currentParent.ChildParties = children = new List<V1Models.Party>();
        }

        if (currentParent is not null)
        {
            yield return currentParent;
        }
    }
}
