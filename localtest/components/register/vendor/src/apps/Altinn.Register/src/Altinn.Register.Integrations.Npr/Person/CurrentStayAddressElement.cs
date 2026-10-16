using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a historical current-stay address entry for a person.
/// </summary>
/// <source>folkeregisteret.tilgjengeliggjoering.person.v1.Oppholdsadresse</source>
public sealed record CurrentStayAddressElement
    : HistoricalElement
{
    /// <summary>
    /// Gets the confidentiality level applied to the address.
    /// </summary>
    [JsonPropertyName("adressegradering")]
    public NonExhaustiveEnum<AddressConfidentialityLevel> ConfidentialityLevel { get; init; }
        = AddressConfidentialityLevel.Unclassified;

    /// <summary>
    /// Gets a value indicating whether the current-stay address is unknown.
    /// </summary>
    [JsonPropertyName("adressenErUkjent")]
    public bool IsUnknown { get; init; }

    /// <summary>
    /// Gets the international address when the stay address is registered abroad.
    /// </summary>
    [JsonPropertyName("utenlandskAdresse")]
    public InternationalMailingAddress? InternationalMailingAddress { get; init; }

    /// <summary>
    /// Gets the street address when the stay address is registered as a street address.
    /// </summary>
    [JsonPropertyName("vegadresse")]
    public StreetAddress? StreetAddress { get; init; }

    /// <summary>
    /// Gets the cadastral address when the stay address is registered as a cadastral address.
    /// </summary>
    [JsonPropertyName("matrikkeladresse")]
    public MatrikkelAddress? MatrikkelAddress { get; init; }
}
