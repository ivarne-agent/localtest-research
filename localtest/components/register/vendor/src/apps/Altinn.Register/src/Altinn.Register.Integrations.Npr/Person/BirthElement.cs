using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a historical birth registration for a person.
/// </summary>
/// <source>folkeregisteret.tilgjengeliggjoering.person.v1.Foedsel</source>
public sealed record BirthElement
    : HistoricalElement
{
    /// <summary>
    /// Gets the registered date of birth.
    /// </summary>
    [JsonPropertyName("foedselsdato")]
    public string? DateOfBirth { get; init; }
}
