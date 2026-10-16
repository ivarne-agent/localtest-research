using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a historical mailing address entry for a person.
/// </summary>
/// <source>folkeregisteret.tilgjengeliggjoering.person.v1.Kontaktadresse</source>
public sealed record MailingAddressElement
    : HistoricalElement
{
    /// <summary>
    /// Gets the confidentiality level applied to the address.
    /// </summary>
    [JsonPropertyName("adressegradering")]
    public NonExhaustiveEnum<AddressConfidentialityLevel> ConfidentialityLevel { get; init; }
        = AddressConfidentialityLevel.Unclassified;

    /// <summary>
    /// Gets the postbox address when the mailing address is registered as a postbox.
    /// </summary>
    [JsonPropertyName("postboksadresse")]
    public PostBoxAddress? PostboxAddress { get; init; }

    /// <summary>
    /// Gets the street address when the mailing address is registered as a street address.
    /// </summary>
    [JsonPropertyName("vegadresse")]
    public StreetAddress? StreetAddress { get; init; }

    /// <summary>
    /// Gets the free-form mailing address.
    /// </summary>
    [JsonPropertyName("postadresseIFrittFormat")]
    public FreeFormMailingAddress? FreeFormMailingAddress { get; init; }
}
