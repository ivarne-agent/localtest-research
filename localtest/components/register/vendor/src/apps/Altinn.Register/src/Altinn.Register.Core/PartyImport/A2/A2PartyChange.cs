namespace Altinn.Register.Core.PartyImport.A2;

/// <summary>
/// Represents a change-event for a party in Altinn 2.
/// </summary>
public sealed record A2PartyChange
    : A2Change
{
    /// <summary>
    /// Gets the party id that changed.
    /// </summary>
    public required int PartyId { get; init; }

    /// <summary>
    /// Gets the party uuid that changed.
    /// </summary>
    public required Guid PartyUuid { get; init; }

    /// <summary>
    /// Gets the time of the change.
    /// </summary>
    public required DateTimeOffset ChangeTime { get; init; }
}
