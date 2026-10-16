using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Core.PartyImport.A2;

/// <summary>
/// Represents profile information about a user in Altinn 2.
/// </summary>
public sealed record A2ProfileRecord
{
    private readonly string? _userName;

    /// <summary>
    /// Gets the user id.
    /// </summary>
    public required uint UserId { get; init; }

    /// <summary>
    /// Gets the user UUID, if any.
    /// </summary>
    public required Guid? UserUuid { get; init; }

    /// <summary>
    /// Gets the external authentication reference associated with this instance.
    /// </summary>
    public required string? ExternalAuthenticationReference { get; init; }

    /// <summary>
    /// Gets the profile type.
    /// </summary>
    public required A2UserProfileType ProfileType { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user is active.
    /// </summary>
    public required bool IsActive { get; init; }

    /// <summary>
    /// Gets the username, if any.
    /// </summary>
    /// <remarks>
    /// Should never be an empty string, but can be null.
    /// </remarks>
    public required string? UserName
    {
        get => _userName;
        init
        {
            if (value is null)
            {
                _userName = null;
                return;
            }

            var trimmed = value.Trim();
            if (trimmed != value)
            {
                ThrowHelper.ThrowInvalidOperationException("Username cannot have leading or trailing whitespace.");
            }

            if (value is "")
            {
                ThrowHelper.ThrowInvalidOperationException("Username cannot be an empty string.");
            }

            _userName = value;
        }
    }

    /// <summary>
    /// Gets the party UUID.
    /// </summary>
    public required Guid PartyUuid { get; init; }

    /// <summary>
    /// Gets the party ID.
    /// </summary>
    public required uint PartyId { get; init; }

    /// <summary>
    /// Gets when the profile/party was last changed.
    /// </summary>
    public required DateTimeOffset? LastChangedAt { get; init; }
}
