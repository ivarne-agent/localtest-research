using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a historical address protection entry for a person.
/// </summary>
/// <source>folkeregisteret.tilgjengeliggjoering.person.v1.GraderingAvAdresse</source>
public sealed record AddressProtectionElement
    : HistoricalElement
{
    /// <summary>
    /// Gets the confidentiality level applied to the address.
    /// </summary>
    [JsonPropertyName("graderingsnivaa")]
    public NonExhaustiveEnum<AddressConfidentialityLevel> ConfidentialityLevel { get; init; }
        = AddressConfidentialityLevel.Unclassified;
}
