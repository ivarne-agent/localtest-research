using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a postal place and postal code.
/// </summary>
public sealed record PostalArea
{
    /// <summary>
    /// Gets the postal place name.
    /// </summary>
    [JsonPropertyName("poststedsnavn")]
    public string? PostalName { get; init; }

    /// <summary>
    /// Gets the postal code.
    /// </summary>
    [JsonPropertyName("postnummer")]
    public string? PostalCode { get; init; }
}
