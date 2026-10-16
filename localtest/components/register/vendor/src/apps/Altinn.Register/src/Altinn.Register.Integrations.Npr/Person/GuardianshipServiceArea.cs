using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a service area covered by a guardianship.
/// </summary>
public sealed record GuardianshipServiceArea
{
    /// <summary>
    /// Gets the organization responsible for the guardianship service.
    /// </summary>
    [JsonPropertyName("vergeTjenestevirksomhet")]
    public string? ServiceOwner { get; init; }

    /// <summary>
    /// Gets the guardianship task within the service area.
    /// </summary>
    [JsonPropertyName("vergeTjenesteoppgave")]
    public string? ServiceTask { get; init; }
}
