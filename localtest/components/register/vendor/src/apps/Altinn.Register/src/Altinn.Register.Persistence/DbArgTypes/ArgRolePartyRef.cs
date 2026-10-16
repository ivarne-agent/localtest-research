using System.Diagnostics;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Persistence.DbArgTypes;

/// <summary>
/// C# counterpart of <c>register.arg_role_party_ref</c>.
/// </summary>
internal sealed record ArgRolePartyRef
{
    /// <summary>
    /// Creates a new instance of <see cref="ArgRolePartyRef"/> with only the party uuid.
    /// This can be used when the party is already known, and we want to avoid looking up the party again.
    /// </summary>
    /// <param name="partyUuid">The party uuid.</param>
    /// <returns>A new instance of <see cref="ArgRolePartyRef"/> with the specified party uuid.</returns>
    public static ArgRolePartyRef CreateUuid(Guid partyUuid)
        => new()
        {
            PartyUuid = partyUuid,
            PersonIdentifier = null,
            OrganizationNumber = null,
            PersonName = null,
            MailingAddress = null,
        };

    /// <summary>
    /// Creates a new instance of <see cref="ArgRolePartyRef"/> with only the organization number.
    /// This can be used when the organization is already known, and we want to avoid looking up the organization again.
    /// </summary>
    /// <param name="orgNumber">The organization number.</param>
    /// <returns>A new instance of <see cref="ArgRolePartyRef"/> with the specified organization number.</returns>
    public static ArgRolePartyRef CreateOrg(OrganizationIdentifier orgNumber)
        => new()
        {
            PartyUuid = null,
            PersonIdentifier = null,
            OrganizationNumber = orgNumber.ToString(),
            PersonName = null,
            MailingAddress = null,
        };

    /// <summary>
    /// Creates a new instance of <see cref="ArgRolePartyRef"/> with the person identifier, name and mailing address.
    /// </summary>
    /// <param name="personIdentifier">The person identifier.</param>
    /// <param name="personName">The name of the person.</param>
    /// <param name="mailingAddress">The mailing address of the person.</param>
    /// <returns>A new instance of <see cref="ArgRolePartyRef"/> with the specified person details.</returns>
    public static ArgRolePartyRef CreatePerson(PersonIdentifier personIdentifier, PersonName? personName, MailingAddressRecord? mailingAddress)
        => new()
        {
            PartyUuid = null,
            PersonIdentifier = personIdentifier.ToString(),
            OrganizationNumber = null,
            PersonName = personName switch
            {
                null => null,
                _ => new ArgRolePartyRefPersonName
                {
                    FirstName = personName.FirstName,
                    MiddleName = personName.MiddleName,
                    LastName = personName.LastName,
                    ShortName = personName.ShortName,
                    DisplayName = personName.DisplayName,
                },
            },
            MailingAddress = mailingAddress,
        };

    /// <summary>
    /// Creates a new instance of <see cref="ArgRolePartyRef"/> from a <see cref="PartyExternalRoleAssignmentPartyRef"/>.
    /// </summary>
    /// <param name="partyRef">The party reference.</param>
    /// <returns>A new instance of <see cref="ArgRolePartyRef"/>.</returns>
    public static ArgRolePartyRef From(PartyExternalRoleAssignmentPartyRef partyRef)
        => partyRef switch
        {
            PartyExternalRoleAssignmentPartyRef.PartyUuid uuid => CreateUuid(uuid.Uuid),
            PartyExternalRoleAssignmentPartyRef.Organization org => CreateOrg(org.OrganizationIdentifier),
            PartyExternalRoleAssignmentPartyRef.Person person => CreatePerson(person.PersonIdentifier, person.Name, person.MailingAddress),
            _ => throw new UnreachableException(),
        };

    /// <summary>
    /// Gets the uuid of the party, if the uuid is know.
    /// </summary>
    public required Guid? PartyUuid { get; init; }

    /// <summary>
    /// Gets the person identifier of the party, if the party is a person.
    /// </summary>
    public required string? PersonIdentifier { get; init; }

    /// <summary>
    /// Gets the organization number of the party, if the party is an organization.
    /// </summary>
    public required string? OrganizationNumber { get; init; }

    /// <summary>
    /// Gets the name of the person.
    /// </summary>
    public required ArgRolePartyRefPersonName? PersonName { get; init; }

    /// <summary>
    /// Gets the mailing address of the person.
    /// </summary>
    public required MailingAddressRecord? MailingAddress { get; init; }
}
