using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a historical person status entry for a person.
/// </summary>
/// <source>folkeregisteret.tilgjengeliggjoering.person.v1.Folkeregisterpersonstatus</source>
public sealed record PersonStatusElement
    : HistoricalElement
{
    /// <summary>
    /// Gets the person's status.
    /// </summary>
    [JsonPropertyName("status")]
    public NonExhaustiveEnum<PersonStatus>? Status { get; init; }
}
