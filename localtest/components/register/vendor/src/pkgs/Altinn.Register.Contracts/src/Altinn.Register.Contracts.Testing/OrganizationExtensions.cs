using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts.Testing;

/// <summary>
/// Extensions for <see cref="Organization"/>.
/// </summary>
public static class OrganizationExtensions
{
    extension(Organization)
    {
        /// <summary>
        /// Creates a minimal "valid" organization model, with all optional fields set to <see cref="FieldValue.Unset"/>.
        /// </summary>
        /// <param name="organizationIdentifier">The organization identifier.</param>
        /// <param name="uuid">Optional uuid for the organization. If unspecified, a <see cref="Guid.NewGuid()"/> is used.</param>
        /// <returns>A minimal <see cref="Organization"/>.</returns>
        public static Organization Minimal(
            OrganizationIdentifier organizationIdentifier,
            Guid uuid = default)
        {
            PartyExtensions.EnsureUuid(ref uuid);

            return new Organization
            {
                Uuid = uuid,
                ExternalUrn = NonExhaustive.Create<PartyExternalRefUrn>(PartyExternalRefUrn.OrganizationId.Create(organizationIdentifier)),
                PartyId = FieldValue.Unset,
                DisplayName = FieldValue.Unset,
                CreatedAt = FieldValue.Unset,
                ModifiedAt = FieldValue.Unset,
                IsDeleted = FieldValue.Unset,
                DeletedAt = FieldValue.Unset,
                User = FieldValue.Unset,
                VersionId = 1UL,

                OrganizationIdentifier = organizationIdentifier,
                Source = FieldValue.Unset,
                UnitStatus = FieldValue.Unset,
                UnitType = FieldValue.Unset,
                TelephoneNumber = FieldValue.Unset,
                MobileNumber = FieldValue.Unset,
                FaxNumber = FieldValue.Unset,
                EmailAddress = FieldValue.Unset,
                InternetAddress = FieldValue.Unset,
                MailingAddress = FieldValue.Unset,
                BusinessAddress = FieldValue.Unset,
            };
        }

        /// <inheritdoc cref="Minimal(OrganizationIdentifier, Guid)"/>
        public static Organization Minimal(
            string organizationIdentifier,
            Guid uuid = default)
            => Minimal(OrganizationIdentifier.Parse(organizationIdentifier), uuid);
    }
}
