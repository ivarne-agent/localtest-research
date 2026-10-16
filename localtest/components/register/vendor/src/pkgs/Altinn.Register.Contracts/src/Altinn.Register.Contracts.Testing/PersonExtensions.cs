using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts.Testing;

/// <summary>
/// Extensions for <see cref="Person"/>.
/// </summary>
public static class PersonExtensions
{
    extension(Person)
    {
        /// <summary>
        /// Creates a minimal "valid" person model, with all optional fields set to <see cref="FieldValue.Unset"/>.
        /// </summary>
        /// <param name="personIdentifier">The person identifier.</param>
        /// <param name="uuid">Optional uuid for the person. If unspecified, a <see cref="Guid.NewGuid()"/> is used.</param>
        /// <returns>A minimal <see cref="Person"/>.</returns>
        public static Person Minimal(
            PersonIdentifier personIdentifier,
            Guid uuid = default)
        {
            PartyExtensions.EnsureUuid(ref uuid);

            return new Person
            {
                Uuid = uuid,
                ExternalUrn = NonExhaustive.Create<PartyExternalRefUrn>(PartyExternalRefUrn.PersonId.Create(personIdentifier)),
                PartyId = FieldValue.Unset,
                DisplayName = FieldValue.Unset,
                CreatedAt = FieldValue.Unset,
                ModifiedAt = FieldValue.Unset,
                IsDeleted = FieldValue.Unset,
                DeletedAt = FieldValue.Unset,
                User = FieldValue.Unset,
                VersionId = 1UL,

                PersonIdentifier = personIdentifier,
                Source = FieldValue.Unset,
                FirstName = FieldValue.Unset,
                MiddleName = FieldValue.Unset,
                LastName = FieldValue.Unset,
                ShortName = FieldValue.Unset,
                Address = FieldValue.Unset,
                MailingAddress = FieldValue.Unset,
                DateOfBirth = FieldValue.Unset,
                DateOfDeath = FieldValue.Unset,
            };
        }

        /// <inheritdoc cref="Minimal(PersonIdentifier, Guid)"/>
        public static Person Minimal(
            string personIdentifier,
            Guid uuid = default)
            => Minimal(PersonIdentifier.Parse(personIdentifier), uuid);
    }
}
