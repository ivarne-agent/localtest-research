using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a mailing address expressed as free-form lines.
/// </summary>
public sealed record FreeFormMailingAddress
{
    /// <summary>
    /// Gets the address lines.
    /// </summary>
    [JsonPropertyName("adresselinje")]
    public ImmutableValueArray<string> AddressLines { get; init; }

    /// <summary>
    /// Gets the ISO country code for the address.
    /// </summary>
    [JsonPropertyName("landkode")]
    public string? CountryCode { get; init; }

    /// <summary>
    /// Gets the postal place for the address.
    /// </summary>
    [JsonPropertyName("poststed")]
    public PostalArea? PostalArea { get; init; }
}
