using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Core.Parties.Records;

/// <summary>
/// Represents a party type.
/// </summary>
[StringEnumConverter(JsonKnownNamingPolicy.KebabCaseLower)]
public enum PartyRecordType
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
