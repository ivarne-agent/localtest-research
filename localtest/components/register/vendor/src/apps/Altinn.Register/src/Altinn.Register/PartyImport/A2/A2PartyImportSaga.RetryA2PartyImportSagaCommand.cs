using Altinn.Register.Core.Parties.Records;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// Saga for importing parties from A2.
/// </summary>
public partial class A2PartyImportSaga
{
    /// <inheritdoc/>
    public async Task Handle(RetryA2PartyImportSagaCommand message, CancellationToken cancellationToken)
    {
        if (!State.PartyIdentifier.HasValue)
        {
            throw new InvalidOperationException("PartyIdentifier is not set");
        }

        if (State.Party is null)
        {
            throw new InvalidOperationException("Party is not set");
        }

        switch (State.Party)
        {
            case OrganizationRecord org when org.Source.HasValue && org.Source.Value == Contracts.OrganizationSource.RegisteredWithSkatteetaten:
                if (!State.PartyIdentifier.TryGetValue(out Contracts.OrganizationIdentifier? organizationIdentifier))
                {
                    ThrowHelper.ThrowInvalidOperationException("PartyIdentifier is not an organization identifier");
                }

                State.Clear();
                await HandleImportSireParty(organizationIdentifier, cancellationToken);
                return;

            case PersonRecord person when person.Source.HasValue && person.Source.Value == Contracts.PersonSource.NationalPopulationRegister:
                if (!State.PartyIdentifier.TryGetValue(out Contracts.PersonIdentifier? personIdentifier))
                {
                    ThrowHelper.ThrowInvalidOperationException("PartyIdentifier is not a person identifier");
                }

                State.Clear();
                await HandleImportNprParty(personIdentifier, cancellationToken);
                return;
        }

        ThrowHelper.ThrowInvalidOperationException("Party source is not set or not supported for retry");
    }
}
