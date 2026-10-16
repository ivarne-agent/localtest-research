using System.Text.Json.Serialization;

namespace Altinn.Register.Core.Parties;

/// <summary>
/// Represents the transformations that can be applied to a list of parties.
/// </summary>
/// <remarks>
/// The numerical value of the items are inconsequential, and can safely be
/// modified as long as each item only consists of a single bit set. However,
/// the order <strong>MUST</strong> remain in sync with the order in which
/// the transforms are applied by the database. This includes the numerical
/// order, not just the lexical one.
/// </remarks>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PartyListTransforms
    : byte
{
    /// <summary>
    /// Do not apply any transforms to the party list.
    /// </summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// Replace each party with its corresponding main unit.
    /// </summary>
    [JsonStringEnumMemberName("replace-with.main-units")]
    ReplaceWithMainUnits = 1 << 0,

    /// <summary>
    /// Include each party's corresponding sub-units in the party list.
    /// </summary>
    [JsonStringEnumMemberName("include.sub-units")]
    IncludeSubUnits = 1 << 1,
}
