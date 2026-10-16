using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a historical residential address entry for a person.
/// </summary>
/// <source>folkeregisteret.tilgjengeliggjoering.person.v1.Bostedsadresse</source>
public sealed record ResidentialAddressElement
    : HistoricalElement
{
    /// <summary>
    /// Gets the confidentiality level applied to the address.
    /// </summary>
    [JsonPropertyName("adressegradering")]
    public NonExhaustiveEnum<AddressConfidentialityLevel> ConfidentialityLevel { get; init; }
        = AddressConfidentialityLevel.Unclassified;

    /// <summary>
    /// Gets the street address when the residential address is registered as a street address.
    /// </summary>
    [JsonPropertyName("vegadresse")]
    public StreetAddress? StreetAddress { get; init; }

    /// <summary>
    /// Gets the unknown residential address details when no street address is registered.
    /// </summary>
    [JsonPropertyName("ukjentBosted")]
    public Unknown? UnknownResidentialAddress { get; init; }

    /// <summary>
    /// Gets the cadastral address when the stay address is registered as a cadastral address.
    /// </summary>
    [JsonPropertyName("matrikkeladresse")]
    public MatrikkelAddress? MatrikkelAddress { get; init; }

    /// <summary>
    /// Represents a residential address entry where the actual address is unknown.
    /// </summary>
    public sealed record Unknown
    {
        /// <summary>
        /// Gets the municipality registered for the unknown residential address.
        /// </summary>
        [JsonPropertyName("bostedskommune")]
        public string? ResidentialMunicipality { get; init; }
    }
}
