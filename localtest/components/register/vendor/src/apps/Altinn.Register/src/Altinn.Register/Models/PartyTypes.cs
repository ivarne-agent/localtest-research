using System.Text.Json.Serialization;

namespace Altinn.Register.Models;

/// <summary>
/// A list of party types.
/// </summary>
[Flags]
public enum PartyTypes
    : byte
{
    /// <summary>
    /// Specifies that no value or option is selected.
    /// </summary>
    None = default,

    /// <summary>
    /// Person party type.
    /// </summary>
    [JsonStringEnumMemberName("person")]
    Person = 1 << 0,

    /// <summary>
    /// Organization party type.
    /// </summary>
    [JsonStringEnumMemberName("organization")]
    Organization = 1 << 1,

    /// <summary>
    /// Self-identified user party type.
    /// </summary>
    [JsonStringEnumMemberName("self-identified-user")]
    SelfIdentifiedUser = 1 << 2,

    /// <summary>
    /// System user party type.
    /// </summary>
    [JsonStringEnumMemberName("system-user")]
    SystemUser = 1 << 3,

    /// <summary>
    /// Enterprise user party type.
    /// </summary>
    [JsonStringEnumMemberName("enterprise-user")]
    EnterpriseUser = 1 << 4,
}
