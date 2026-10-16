using System.Text.Json.Serialization;

namespace Altinn.Register.Persistence;

/// <summary>
/// Represents the identifiers that can be used to look up parties.
/// </summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PartyLookupIdentifiers
    : ushort
{
    /// <summary>
    /// Do not lookup based on anything (effectively, get all).
    /// </summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// Get by party id.
    /// </summary>
    [JsonStringEnumMemberName("id")]
    PartyId = 1 << 1,

    /// <summary>
    /// Get by party UUID.
    /// </summary>
    [JsonStringEnumMemberName("uuid")]
    PartyUuid = 1 << 2,

    /// <summary>
    /// Get by external URN.
    /// </summary>
    [JsonStringEnumMemberName("external-urn")]
    ExternalUrn = 1 << 3,

    /// <summary>
    /// Get by person identifier.
    /// </summary>
    [JsonStringEnumMemberName("pers.id")]
    PersonIdentifier = 1 << 4,

    /// <summary>
    /// Get by organization identifier.
    /// </summary>
    [JsonStringEnumMemberName("org.id")]
    OrganizationIdentifier = 1 << 5,

    /// <summary>
    /// Get by user id.
    /// </summary>
    [JsonStringEnumMemberName("user.id")]
    UserId = 1 << 6,

    /// <summary>
    /// Get by username.
    /// </summary>
    [JsonStringEnumMemberName("user.name")]
    Username = 1 << 7,

    /// <summary>
    /// Get by self-identified email.
    /// </summary>
    [JsonStringEnumMemberName("self-identified.email")]
    SelfIdentifiedEmail = 1 << 8,
}
