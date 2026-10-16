using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents a self-identified-user type.
/// </summary>
/// <remarks>
/// This enum is explicitly made such that <c><see langword="default"/>(<see cref="SelfIdentifiedUserType"/>)</c> is not a valid value.
/// </remarks>
[StringEnumConverter]
public enum SelfIdentifiedUserType
{
    /// <summary>
    /// Legacy Altinn 2 Self-Identified User
    /// </summary>
    [JsonStringEnumMemberName("legacy")]
    Legacy = 1,

    /// <summary>
    /// Educational (FEIDE or UIDP) Self-Identified User
    /// </summary>
    [JsonStringEnumMemberName("edu")]
    Educational,

    /// <summary>
    /// ID-Porten Email Self-Identified User
    /// </summary>
    [JsonStringEnumMemberName("idporten-email")]
    IdPortenEmail,
}
