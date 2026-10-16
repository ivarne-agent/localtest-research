using System.Text.Json.Serialization;

namespace Altinn.Register.Persistence;

/// <summary>
/// Represents the filters that can be applied when querying a list of parties.
/// </summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PartyListFilters
    : byte
{
    /// <summary>
    /// Do not apply any additional filters to the party list.
    /// </summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// Filter based on one or more party types.
    /// </summary>
    [JsonStringEnumMemberName("type")]
    PartyType = 1 << 0,
}
