using Altinn.Authorization.ModelUtils;
using Altinn.Urn;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Contracts.Testing;

/// <summary>
/// Extensions for <see cref="SelfIdentifiedUser"/>.
/// </summary>
public static class SelfIdentifiedUserExtensions
{
    extension(SelfIdentifiedUser)
    {
        /// <summary>
        /// Creates a minimal "valid" si model, with all optional fields set to <see cref="FieldValue.Unset"/>.
        /// </summary>
        /// <param name="uuid">Optional uuid for the person. If unspecified, a <see cref="Guid.NewGuid()"/> is used.</param>
        /// <returns>A minimal <see cref="SelfIdentifiedUser"/>.</returns>
        public static SelfIdentifiedUser Minimal(
            Guid uuid = default)
        {
            PartyExtensions.EnsureUuid(ref uuid);

            return new SelfIdentifiedUser
            {
                Uuid = uuid,
                ExternalUrn = FieldValue.Null,
                PartyId = FieldValue.Unset,
                DisplayName = FieldValue.Unset,
                CreatedAt = FieldValue.Unset,
                ModifiedAt = FieldValue.Unset,
                IsDeleted = FieldValue.Unset,
                DeletedAt = FieldValue.Unset,
                User = FieldValue.Unset,
                VersionId = 1UL,

                SelfIdentifiedUserType = FieldValue.Unset,
                Email = FieldValue.Unset,
            };
        }

        /// <summary>
        /// Creates a minimal "valid" legacy si model, with all optional fields set to <see cref="FieldValue.Unset"/>.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <param name="uuid">Optional uuid for the person. If unspecified, a <see cref="Guid.NewGuid()"/> is used.</param>
        /// <returns>A minimal <see cref="SelfIdentifiedUser"/>.</returns>
        public static SelfIdentifiedUser MinimalLegacy(
            string username,
            Guid uuid = default)
        {
            Guard.IsNotNullOrEmpty(username);

            return Minimal(uuid) with
            {
                ExternalUrn = NonExhaustive.Create<PartyExternalRefUrn>(PartyExternalRefUrn.LegacySelfIdentifiedUsername.Create(UrnEncoded.Create(username))),
                SelfIdentifiedUserType = NonExhaustiveEnum.Create(SelfIdentifiedUserType.Legacy),
            };
        }

        /// <summary>
        /// Creates a minimal "valid" idporten-email si model, with all optional fields set to <see cref="FieldValue.Unset"/>.
        /// </summary>
        /// <param name="email">The email.</param>
        /// <param name="uuid">Optional uuid for the person. If unspecified, a <see cref="Guid.NewGuid()"/> is used.</param>
        /// <returns>A minimal <see cref="SelfIdentifiedUser"/>.</returns>
        public static SelfIdentifiedUser MinimalEmail(
            string email,
            Guid uuid = default)
        {
            Guard.IsNotNullOrEmpty(email);

            return Minimal(uuid) with
            {
                ExternalUrn = NonExhaustive.Create<PartyExternalRefUrn>(PartyExternalRefUrn.IDPortenEmail.Create(UrnEncoded.Create(email))),
                SelfIdentifiedUserType = NonExhaustiveEnum.Create(SelfIdentifiedUserType.IdPortenEmail),
                Email = email,
            };
        }
    }
}
