using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents a party type.
/// </summary>
/// <remarks>
/// This enum is explicitly made such that <c><see langword="default"/>(<see cref="PartyType"/>)</c> is not a valid value.
/// </remarks>
[StringEnumConverter]
public enum PartyType
    : uint
{
    /// <summary>
    /// Person party type.
    /// </summary>
    [JsonStringEnumMemberName("person")]
    Person = 1,

    /// <summary>
    /// Organization party type.
    /// </summary>
    [JsonStringEnumMemberName("organization")]
    Organization,

    /// <summary>
    /// Self-identified user party type.
    /// </summary>
    [JsonStringEnumMemberName("self-identified-user")]
    SelfIdentifiedUser,

    /// <summary>
    /// System user party type.
    /// </summary>
    [JsonStringEnumMemberName("system-user")]
    SystemUser,

    /// <summary>
    /// Enterprise user party type.
    /// </summary>
    [JsonStringEnumMemberName("enterprise-user")]
    EnterpriseUser,
}
