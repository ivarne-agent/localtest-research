using System.Text;
using Altinn.Authorization.ModelUtils;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents user-information for a party in Altinn Register.
/// </summary>
[FieldValueRecord]
public sealed record PartyUser
{
    /// <summary>
    /// Gets the current active user id.
    /// </summary>
    [JsonPropertyName("userId")]
    public FieldValue<uint> UserId { get; }

    /// <summary>
    /// Gets the username of the party (if any).
    /// </summary>
    // TODO: Make a CaseInsensitiveString type for usernames?
    [JsonPropertyName("username")]
    public FieldValue<string> Username { get; }

    /// <summary>
    /// Gets the (historical) user ids of the party.
    /// </summary>
    /// <remarks>
    /// Should never be empty, but can be null or unset. The first user id is the current one - the rest should be ordered in descending order.
    /// </remarks>
    [JsonPropertyName("userIds")]
    public FieldValue<ImmutableValueArray<uint>> UserIds { get; }

    /// <summary>
    /// Gets the (historical) usernames of the party.
    /// </summary>
    [JsonPropertyName("usernames")]
    public FieldValue<ImmutableValueArray<string>> Usernames { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PartyUser"/> class.
    /// </summary>
    /// <param name="userId">The current active user id.</param>
    /// <param name="username">The username of the party.</param>
    /// <param name="userIds">The user ids of the party.</param>
    public PartyUser(
        FieldValue<uint> userId,
        FieldValue<string> username,
        FieldValue<ImmutableValueArray<uint>> userIds)
        : this(userId, username, userIds, username.Select(ImmutableValueArray.Create))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PartyUser"/> class.
    /// </summary>
    /// <param name="userId">The current active user id.</param>
    /// <param name="username">The username of the party.</param>
    /// <param name="userIds">The user ids of the party.</param>
    /// <param name="usernames">The usernames of the party.</param>
    [JsonConstructor]
    public PartyUser(
        FieldValue<uint> userId,
        FieldValue<string> username,
        FieldValue<ImmutableValueArray<uint>> userIds,
        FieldValue<ImmutableValueArray<string>> usernames)
    {
        if (username.HasValue && userId.IsNull)
        {
            ThrowHelper.ThrowArgumentException(nameof(username), "Username cannot be set if userId is null.");
        }

        if (userIds.HasValue && userIds.Value.Length == 0)
        {
            ThrowHelper.ThrowArgumentException(nameof(userIds), "UserIds cannot be empty.");
        }

        if (userId.HasValue)
        {
            if (!userIds.HasValue)
            {
                ThrowHelper.ThrowArgumentException(nameof(userIds), "UserIds cannot be null if userId is set.");
            }

            if (userIds.Value[0] != userId.Value)
            {
                ThrowHelper.ThrowArgumentException(nameof(userIds), "UserIds must start with the current userId.");
            }
        }

        UserId = userId;
        Username = username;
        UserIds = userIds;
        Usernames = usernames;
    }

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("UserId = ");
        builder.Append(UserId);

        builder.Append(", Username = ");
        builder.Append(Username);

        builder.Append(", UserIds = ");
        if (!UserIds.HasValue)
        {
            builder.Append(UserIds);
        }
        else
        {
            builder.Append("[ ");
            var first = true;
            foreach (var item in UserIds.Value)
            {
                if (!first)
                {
                    builder.Append(", ");
                }

                first = false;
                builder.Append(item);
            }

            if (!first)
            {
                builder.Append(' ');
            }

            builder.Append(']');
        }

        builder.Append(", Usernames = ");
        if (!Usernames.HasValue)
        {
            builder.Append(Usernames);
        }
        else
        {
            builder.Append($"[Count = {Usernames.Value.Length}]");
        }

        return true;
    }
}
