using System.Text.Json.Serialization;

namespace Altinn.Register.Core.Parties.Records;

/// <summary>
/// Represents a system user record type.
/// </summary>
public enum SystemUserRecordType
{
    /// <summary>
    /// A system user for own use.
    /// </summary>
    [JsonStringEnumMemberName("standard")]
    Standard,

    /// <summary>
    /// A system user for client relations.
    /// </summary>
    [JsonStringEnumMemberName("agent")]
    Agent,
}
