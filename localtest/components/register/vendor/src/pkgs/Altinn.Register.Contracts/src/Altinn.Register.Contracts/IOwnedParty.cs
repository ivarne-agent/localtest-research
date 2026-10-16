using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents a party that has an owner.
/// </summary>
public interface IOwnedParty
{
    /// <summary>
    /// Gets the owner of this party.
    /// </summary>
    public FieldValue<PartyRef> Owner { get; }
}
