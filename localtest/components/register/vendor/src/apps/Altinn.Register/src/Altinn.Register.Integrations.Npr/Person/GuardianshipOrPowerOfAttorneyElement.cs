using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a historical guardianship or future power-of-attorney entry for a person.
/// </summary>
public sealed record GuardianshipOrPowerOfAttorneyElement
    : HistoricalElement
{
    /// <summary>
    /// Gets the guardianship details when the entry represents a guardianship.
    /// </summary>
    [JsonPropertyName("verge")]
    public Guardianship? Guardianship { get; init; }
}
