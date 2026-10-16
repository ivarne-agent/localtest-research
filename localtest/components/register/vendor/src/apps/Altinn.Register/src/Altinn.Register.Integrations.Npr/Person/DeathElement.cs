using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a historical death registration for a person.
/// </summary>
/// <source>folkeregisteret.tilgjengeliggjoering.person.v1.Doedsfall</source>
public sealed record DeathElement
    : HistoricalElement
{
    /// <summary>
    /// Gets the registered date of death.
    /// </summary>
    [JsonPropertyName("doedsdato")]
    public string? DateOfDeath { get; init; }
}
