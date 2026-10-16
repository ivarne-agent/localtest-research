using System.Text.Json.Serialization;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Core.Parties;

/// <summary>
/// Represents a reference to a party in the context of an external-role assignment.
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(PartyUuid), typeDiscriminator: "uuid")]
[JsonDerivedType(typeof(Person), typeDiscriminator: "pers")]
[JsonDerivedType(typeof(Organization), typeDiscriminator: "org")]
public abstract record PartyExternalRoleAssignmentPartyRef
{
    private PartyExternalRoleAssignmentPartyRef()
    {
    }

    /// <summary>
    /// A reference to a party by its UUID.
    /// </summary>
    public sealed record PartyUuid
        : PartyExternalRoleAssignmentPartyRef
    {
        /// <summary>
        /// Gets the UUID of the party.
        /// </summary>
        public required Guid Uuid { get; init; }
    }

    /// <summary>
    /// A reference to a party by its person identifier.
    /// </summary>
    public sealed record Person
        : PartyExternalRoleAssignmentPartyRef
    {
        /// <summary>
        /// Gets the person identifier of the party.
        /// </summary>
        public required PersonIdentifier PersonIdentifier { get; init; }

        /// <summary>
        /// Gets the name of the person.
        /// </summary>
        public required PersonName? Name { get; init; }

        /// <summary>
        /// Gets the mailing address of the person.
        /// </summary>
        public required MailingAddressRecord? MailingAddress { get; init; }
    }

    /// <summary>
    /// A reference to a party by its organization identifier.
    /// </summary>
    public sealed record Organization
        : PartyExternalRoleAssignmentPartyRef
    {
        /// <summary>
        /// Gets the organization identifier of the party.
        /// </summary>
        public required OrganizationIdentifier OrganizationIdentifier { get; init; }
    }
}
