using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// Saga for importing parties from A2.
/// </summary>
public partial class A2PartyImportSaga
{
    /// <inheritdoc/>
    public static ValueTask<A2PartyImportSagaData> CreateInitialState(IServiceProvider services, ImportNprPartyCommand command)
        => ValueTask.FromResult(new A2PartyImportSagaData
        {
            PartyIdentifier = command.PersonIdentifier,
            Tracking = command.Tracking,
        });

    /// <inheritdoc/>
    public async Task Handle(ImportNprPartyCommand message, CancellationToken cancellationToken)
        => await HandleImportNprParty(message.PersonIdentifier, cancellationToken);

    private async Task HandleImportNprParty(PersonIdentifier personIdentifier, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        State.Party = new PersonRecord
        {
            PartyUuid = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            PartyId = FieldValue.Unset,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personIdentifier),
            DisplayName = FieldValue.Unset,
            PersonIdentifier = personIdentifier,
            OrganizationIdentifier = FieldValue.Null,
            CreatedAt = now,
            ModifiedAt = now,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            IsDeleted = FieldValue.Unset,
            DeletedAt = FieldValue.Unset,
            VersionId = FieldValue.Unset,

            Source = PersonSource.NationalPopulationRegister,
            FirstName = FieldValue.Unset,
            MiddleName = FieldValue.Unset,
            LastName = FieldValue.Unset,
            ShortName = FieldValue.Unset,
            Address = FieldValue.Unset,
            MailingAddress = FieldValue.Unset,
            DateOfBirth = FieldValue.Unset,
            DateOfDeath = FieldValue.Unset,
        };

        await Enrich(cancellationToken);
    }
}
