using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents the type of dwelling unit for a cadastral address.
/// </summary>
[StringEnumConverter]
public enum MatrikkelUnitType
{
    /// <summary>
    /// Represents a residential dwelling unit, such as an apartment or house.
    /// </summary>
    [JsonStringEnumMemberName("bolig")]
    Bolig = 1,

    /// <summary>
    /// Represents a non-residential dwelling unit, such as a commercial property or other non-living space.
    /// </summary>
    [JsonStringEnumMemberName("annetEnnBolig")]
    AnnetEnnBolig,

    /// <summary>
    /// Represents a recreational dwelling unit, such as a cabin or holiday home.
    /// </summary>
    [JsonStringEnumMemberName("fritidsbolig")]
    Fritidsbolig,

    /// <summary>
    /// Represents a dwelling unit that is not approved for habitation, such as an unapproved building or structure.
    /// </summary>
    [JsonStringEnumMemberName("ikkeGodkjentBolig")]
    IkkeGodkjentBolig,

    /// <summary>
    /// Represents a dwelling unit that is not numbered, such as a common area or shared space within a property.
    /// </summary>
    [JsonStringEnumMemberName("unummerertBruksenhet")]
    UnummerertBruksenhet,
}
