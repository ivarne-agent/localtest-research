using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a postbox mailing address.
/// </summary>
public sealed record PostBoxAddress
{
    /// <summary>
    /// Gets the owner of the postbox.
    /// </summary>
    [JsonPropertyName("postbokseier")]
    public string? Owner { get; init; }

    /// <summary>
    /// Gets the postbox identifier.
    /// </summary>
    [JsonPropertyName("postboks")]
    public string? PostBox { get; init; }

    /// <summary>
    /// Gets the postal place for the postbox.
    /// </summary>
    [JsonPropertyName("poststed")]
    public PostalArea? PostalArea { get; init; }
}
