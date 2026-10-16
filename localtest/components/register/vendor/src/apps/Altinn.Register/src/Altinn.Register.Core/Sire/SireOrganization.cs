using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Core.Sire;

/// <summary>
/// Represents an organization in SIRE. This is a subset of the properties returned by SIRE, and only contains the properties that are relevant for our use cases.
/// </summary>
public sealed record SireOrganization
{
    /// <summary>
    /// Gets the 9-digit organization identifier.
    /// </summary>
    public required OrganizationIdentifier OrganizationIdentifier { get; init; }

    /// <summary>
    /// Gets the organization name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the organization form (e.g. "AS", "ANS"). Stored as free text.
    /// </summary>
    public required string UnitType { get; init; }

    /// <summary>
    /// Gets the unit status. Set to "S" when the organization has been deleted and 'E' for all other cases.
    /// </summary>
    public required string? UnitStatus { get; init; }

    /// <summary>
    /// Gets the moment the organization was deleted, or <see langword="null"/> if it has
    /// not been deleted. Derived from SIRE's <c>slettetdato</c> (date-only) field by
    /// interpreting it as midnight UTC.
    /// </summary>
    public required DateTimeOffset? DeletedAt { get; init; }

    /// <summary>
    /// Gets the mailing address, or null if no address is available.
    /// </summary>
    public required MailingAddressRecord? MailingAddress { get; init; }

    /// <summary>
    /// Gets the last updated timestamp from the postal address.
    /// </summary>
    public required DateTimeOffset? LastUpdated { get; init; }

    /// <summary>
    /// Gets the business relationships (virksomhetsrelasjon).
    /// </summary>
    public required ImmutableValueArray<SireBusinessRelationship> BusinessRelationships { get; init; }
}
