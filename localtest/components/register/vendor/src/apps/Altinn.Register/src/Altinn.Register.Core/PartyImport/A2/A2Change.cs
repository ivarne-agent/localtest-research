namespace Altinn.Register.Core.PartyImport.A2;

/// <summary>
/// Represents a base class for changes in Altinn 2.
/// </summary>
public abstract record A2Change
{
    /// <summary>
    /// Gets the id of this change.
    /// </summary>
    public required uint ChangeId { get; init; }
}
