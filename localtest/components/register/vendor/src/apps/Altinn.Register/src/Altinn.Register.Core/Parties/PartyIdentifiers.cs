using System.Text.Json.Serialization;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;
using V1Models = Altinn.Register.Contracts.V1;

namespace Altinn.Register.Core.Parties;

/// <summary>
/// A set of identifiers for a party.
/// </summary>
public record PartyIdentifiers
{
    /// <summary>
    /// Gets the party id.
    /// </summary>
    [JsonPropertyName("partyId")]
    public required uint PartyId { get; init; }

    /// <summary>
    /// Gets the party uuid.
    /// </summary>
    [JsonPropertyName("partyUuid")]
    public required Guid Uuid { get; init; }

    /// <summary>
    /// Gets the organization identifier of the party, if applicable.
    /// </summary>
    [JsonPropertyName("orgNumber")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrganizationIdentifier? OrganizationIdentifier { get; init; }

    /// <summary>
    /// Gets the person identifier of the party, if applicable and included.
    /// </summary>
    [JsonPropertyName("ssn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PersonIdentifier? PersonIdentifier { get; init; }

    /// <summary>
    /// Create a new <see cref="PartyIdentifiers"/> from a <see cref="V1Models.Party"/>.
    /// </summary>
    /// <param name="party">The party from which to create the identifiers.</param>
    /// <param name="includePersonIdentifier">Whether or not to include the person identifier, if any.</param>
    /// <returns>A <see cref="PartyIdentifiers"/>.</returns>
    public static PartyIdentifiers Create(V1Models.Party party, bool includePersonIdentifier = false)
    {
        return new PartyIdentifiers
        {
            PartyId = checked((uint)party.PartyId),
            Uuid = party.PartyUuid,
            OrganizationIdentifier = ParseOrganizationIdentifier(party.OrgNumber),
            PersonIdentifier = includePersonIdentifier ? ParsePersonIdentifier(party.SSN) : null,
        };

        static OrganizationIdentifier? ParseOrganizationIdentifier(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : OrganizationIdentifier.TryParse(value, provider: null, out var result) ? result : null;

        static PersonIdentifier? ParsePersonIdentifier(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : PersonIdentifier.TryParse(value, provider: null, out var result) ? result : null;
    }

    /// <summary>
    /// Create a new <see cref="PartyIdentifiers"/> from a <see cref="PartyRecord"/>.
    /// </summary>
    /// <param name="party">The party record from which to create the identifiers.</param>
    /// <param name="includePersonIdentifier">Whether or not to include the person identifier, if any.</param>
    /// <returns>A <see cref="PartyIdentifiers"/>.</returns>
    public static PartyIdentifiers Create(PartyRecord party, bool includePersonIdentifier = false)
    {
        return new PartyIdentifiers
        {
            PartyId = party.PartyId.Value,
            Uuid = party.PartyUuid.Value,
            OrganizationIdentifier = party.OrganizationIdentifier.HasValue ? party.OrganizationIdentifier.Value : null,
            PersonIdentifier = includePersonIdentifier && party.PersonIdentifier.HasValue ? party.PersonIdentifier.Value : null,
        };
    }
}
