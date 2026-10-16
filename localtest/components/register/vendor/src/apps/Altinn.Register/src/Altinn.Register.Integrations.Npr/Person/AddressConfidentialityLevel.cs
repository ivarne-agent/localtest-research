using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents the confidentiality level applied to an address in NPR data.
/// </summary>
/// <source>folkeregisteret.tilgjengeliggjoering.person.v1.Adressegradering</source>
[StringEnumConverter]
public enum AddressConfidentialityLevel
    : byte
{
    /// <summary>
    /// No address protection is applied.
    /// </summary>
    [JsonStringEnumMemberName("ugradert")]
    Unclassified = 1,

    /// <summary>
    /// The address is registered as a client address.
    /// </summary>
    [JsonStringEnumMemberName("klientadresse")]
    ClientAddress,

    /// <summary>
    /// The address is marked confidential.
    /// </summary>
    [JsonStringEnumMemberName("fortrolig")]
    Confidential,

    /// <summary>
    /// The address is marked strictly confidential.
    /// </summary>
    [JsonStringEnumMemberName("strengtFortrolig")]
    StrictlyConfidential,
}
