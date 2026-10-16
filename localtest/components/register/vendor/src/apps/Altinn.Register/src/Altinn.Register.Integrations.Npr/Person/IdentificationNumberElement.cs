using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a historical national identity number entry for a person.
/// </summary>
/// <source>folkeregisteret.tilgjengeliggjoering.person.v1.Folkeregisteridentifikator</source>
public sealed record IdentificationNumberElement
    : HistoricalElement
{
    /// <summary>
    /// Gets the person's Norwegian national identity number or D-number.
    /// </summary>
    [JsonPropertyName("foedselsEllerDNummer")]
    public string? IdentificationNumber { get; init; }
}
