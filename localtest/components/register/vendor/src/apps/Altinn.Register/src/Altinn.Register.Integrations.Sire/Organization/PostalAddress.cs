using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Sire.Organization;

/// <summary>
/// Represents a postal address from the SIRE API.
/// </summary>
internal sealed record PostalAddress
{
    /// <summary>
    /// Gets the timestamp when the address record was last updated in SIRE.
    /// </summary>
    [JsonPropertyName("ajourholdstidspunkt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Gets the timestamp from which this address is valid. <see langword="null"/> means "valid since forever".
    /// </summary>
    [JsonPropertyName("gyldighetstidspunkt")]
    public DateTimeOffset? ValidFrom { get; init; }

    /// <summary>
    /// Gets the timestamp at which this address stopped being valid. <see langword="null"/> means "still valid".
    /// </summary>
    [JsonPropertyName("opphoerstidspunkt")]
    public DateTimeOffset? ValidTo { get; init; }

    /// <summary>
    /// Gets the Norwegian address, if applicable.
    /// </summary>
    [JsonPropertyName("norskAdresse")]
    public NorwegianAddress? NorwegianAddress { get; init; }

    /// <summary>
    /// Gets the international address, if applicable.
    /// </summary>
    [JsonPropertyName("utenlandskAdresse")]
    public InternationalAddress? InternationalAddress { get; init; }
}
