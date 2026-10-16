using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents a system-user type.
/// </summary>
/// <remarks>
/// This enum is explicitly made such that <c><see langword="default"/>(<see cref="SystemUserType"/>)</c> is not a valid value.
/// </remarks>
[StringEnumConverter]
public enum SystemUserType
    : uint
{
    /// <summary>
    /// A "standard" system user, used for system-access for own relations.
    /// </summary>
    [JsonStringEnumMemberName("first-party-system-user")]
    FirstPartySystemUser = 1,

    /// <summary>
    /// An "agent" system user, used for system-access for client relations.
    /// </summary>
    [JsonStringEnumMemberName("client-party-system-user")]
    ClientPartySystemUser,
}
