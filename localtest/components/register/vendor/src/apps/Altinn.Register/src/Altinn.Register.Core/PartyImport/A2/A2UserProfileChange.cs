namespace Altinn.Register.Core.PartyImport.A2;

/// <summary>
/// Represents a change-event for a user profile in Altinn 2.
/// </summary>
public sealed record A2UserProfileChange
    : A2Change
{
    /// <summary>
    /// Gets the user id that changed.
    /// </summary>
    public required uint UserId { get; init; }

    /// <summary>
    /// Gets the user uuid that changed.
    /// </summary>
    public required Guid UserUuid { get; init; }

    /// <summary>
    /// Gets the party uuid of the owner of the user profile.
    /// </summary>
    public required Guid OwnerPartyUuid { get; init; }

    /// <summary>
    /// Gets the user name of the user profile.
    /// </summary>
    public required string? UserName { get; init; }

    /// <summary>
    /// Gets whether the user profile is deleted or not.
    /// </summary>
    public required bool IsDeleted { get; init; }

    /// <summary>
    /// Gets the type of the user profile.
    /// </summary>
    public required A2UserProfileType ProfileType { get; init; }
}
