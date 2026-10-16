using System.Diagnostics.CodeAnalysis;

namespace Altinn.Register.Core.A2.SblProfile;

/// <summary>
/// Lookup result wrapper for <see cref="ISblProfileBridgeClient.LookupUser"/>.
/// </summary>
/// <param name="Profile">The user profile if one was found; otherwise <see langword="null"/>.</param>
public readonly record struct SblUserLookup(SblUserProfile? Profile)
{
    /// <summary>
    /// Gets a value indicating whether a user profile was found.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Profile))]
    public bool Found => Profile is not null;

    /// <summary>
    /// A lookup result representing "not found".
    /// </summary>
    public static SblUserLookup NotFound { get; } = new(null);
}
