using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents guardianship details registered for a person.
/// </summary>
public sealed record Guardianship
{
    /// <summary>
    /// Gets the national identity number of the guardian.
    /// </summary>
    [JsonPropertyName("foedselsEllerDNummer")]
    public string? GuardianIdentifier { get; init; }

    /// <summary>
    /// Gets the service areas covered by the guardianship.
    /// </summary>
    [JsonPropertyName("tjenesteomraade")]
    public ImmutableValueArray<GuardianshipServiceArea> ServiceAreas { get; init; }
}
