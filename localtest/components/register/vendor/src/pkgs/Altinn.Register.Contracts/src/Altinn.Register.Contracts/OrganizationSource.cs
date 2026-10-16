using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts;

/// <summary>
/// Specifies the source of an organization-party.
/// </summary>
[StringEnumConverter]
public enum OrganizationSource
    : byte
{
    /// <summary>
    /// The Norwegian Central Coordinating Register for Legal Entities.
    /// </summary>
    [JsonStringEnumMemberName("ccr")]
    CentralCoordinatingRegister = 1,

    /// <summary>
    /// Skatteetaten-registrerte selskaper (SIRE).
    /// </summary>
    [JsonStringEnumMemberName("sire")]
    RegisteredWithSkatteetaten
}
