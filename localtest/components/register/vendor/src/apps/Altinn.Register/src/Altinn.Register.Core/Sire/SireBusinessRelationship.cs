using Altinn.Register.Contracts;

namespace Altinn.Register.Core.Sire;

/// <summary>
/// Represents a business relationship (virksomhetsrelasjon) as returned by the SIRE API.
/// </summary>
public sealed record SireBusinessRelationship
{
    /// <summary>
    /// Gets the external role identifier (e.g. "daglig-leder").
    /// </summary>
    public required string RoleIdentifier { get; init; }

    /// <summary>
    /// Gets the related person identifier, if the related party is a person.
    /// </summary>
    public required PersonIdentifier? RelatedPersonIdentifier { get; init; }

    /// <summary>
    /// Gets the related organization identifier, if the related party is an organization.
    /// </summary>
    public required OrganizationIdentifier? RelatedOrganizationIdentifier { get; init; }
}
