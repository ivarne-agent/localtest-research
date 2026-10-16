namespace Altinn.Register.Core.Parties;

/// <summary>
/// Represents a party source.
/// </summary>
public enum PartySource
{
    /// <summary>
    /// The Norwegian Central Coordinating Register for Legal Entities.
    /// </summary>
    CentralCoordinatingRegister,

    /// <summary>
    /// The Norwegian National Population Register.
    /// </summary>
    NationalPopulationRegister,

    /// <summary>
    /// Registered with Skatteetaten.
    /// </summary>
    RegisteredWithSkatteetaten
}
