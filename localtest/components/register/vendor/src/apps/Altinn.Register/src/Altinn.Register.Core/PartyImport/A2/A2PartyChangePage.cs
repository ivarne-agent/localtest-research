using System.Collections.Immutable;

namespace Altinn.Register.Core.PartyImport.A2;

/// <summary>
/// A page of <see cref="A2PartyChange"/>s.
/// </summary>
public sealed class A2PartyChangePage(ImmutableArray<A2PartyChange> parties, uint lastKnownChangeId)
    : A2ChangePage<A2PartyChange>(parties, lastKnownChangeId)
{
}
