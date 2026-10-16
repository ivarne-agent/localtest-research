using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a historical international mailing address entry for a person.
/// </summary>
/// <source>folkeregisteret.tilgjengeliggjoering.person.v1.KontaktadresseIUtlandet</source>
public sealed record InternationalMailingAddressElement
    : HistoricalElement
{
    /// <summary>
    /// Gets the confidentiality level applied to the address.
    /// </summary>
    [JsonPropertyName("adressegradering")]
    public NonExhaustiveEnum<AddressConfidentialityLevel> ConfidentialityLevel { get; init; }
        = AddressConfidentialityLevel.Unclassified;

    /// <summary>
    /// Gets the structured international mailing address.
    /// </summary>
    [JsonPropertyName("utenlandskAdresse")]
    public InternationalMailingAddress? InternationalMailingAddress { get; init; }

    /// <summary>
    /// Gets the free-form international mailing address.
    /// </summary>
    [JsonPropertyName("utenlandskAdresseIFrittFormat")]
    public InternationalFreeFormMailingAddress? FreeFormInternationalMailingAddress { get; init; }
}
