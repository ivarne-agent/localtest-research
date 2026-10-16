using System.Collections.Immutable;

namespace Altinn.Register.Core.PartyImport.A2;

/// <summary>
/// A page of <see cref="A2UserProfileChange"/>s.
/// </summary>
public sealed class A2UserProfileChangePage(ImmutableArray<A2UserProfileChange> profiles, uint lastKnownChangeId)
    : A2ChangePage<A2UserProfileChange>(profiles, lastKnownChangeId)
{
}
