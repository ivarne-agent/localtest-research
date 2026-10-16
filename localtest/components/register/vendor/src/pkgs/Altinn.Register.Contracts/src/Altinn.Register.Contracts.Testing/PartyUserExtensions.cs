using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts.Testing;

/// <summary>
/// Extensions for <see cref="PartyUser"/>.
/// </summary>
public static class PartyUserExtensions
{
    extension(PartyUser)
    {
        /// <summary>
        /// Creates a new <see cref="PartyUser"/> with a userid and optional username.
        /// </summary>
        /// <param name="userId">The userid.</param>
        /// <param name="username">The username.</param>
        /// <returns>A <see cref="PartyUser"/>.</returns>
        public static PartyUser Create(uint userId, FieldValue<string> username = default)
            => new(
                userId: userId,
                username: username,
                userIds: ImmutableValueArray.Create(userId),
                usernames: username.HasValue ? [username.Value] : ImmutableValueArray<string>.Empty);
    }
}
