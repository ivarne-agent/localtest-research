using System.Runtime.CompilerServices;

using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Mediator;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.Core.Utils;

namespace Altinn.Register.Core.Operations;

#pragma warning disable SA1649 // File name should match first type name

/// <summary>
/// Request to get a set of v1 party identifiers.
/// </summary>
/// <param name="PartyIds">The party ids.</param>
/// <param name="PartyUuids">The party uuids.</param>
/// <param name="OrganizationIdentifiers">The organization identifiers.</param>
public readonly record struct GetV1PartyIdentifiersRequest(
    IReadOnlyList<uint>? PartyIds,
    IReadOnlyList<Guid>? PartyUuids,
    IReadOnlyList<OrganizationIdentifier>? OrganizationIdentifiers)
    : IRequest<IAsyncEnumerable<PartyIdentifiers>>;

/// <summary>
/// Get party identifiers from A2.
/// </summary>
internal sealed class GetV1PartyIdentifiersFromA2RequestHandler(IV1PartyService partyService)
    : IRequestHandler<GetV1PartyIdentifiersRequest, IAsyncEnumerable<PartyIdentifiers>>
{
    /// <inheritdoc/>
    public ValueTask<Result<IAsyncEnumerable<PartyIdentifiers>>> Handle(GetV1PartyIdentifiersRequest request, CancellationToken cancellationToken)
    {
        var parties = AsyncEnumerable.Empty<Contracts.V1.Party>();

        if (request.PartyIds is { Count: > 0 })
        {
            parties = parties.Merge(partyService.GetPartiesById(request.PartyIds.Select(static v => checked((int)v)), cancellationToken));
        }

        if (request.PartyUuids is { Count: > 0 })
        {
            parties = parties.Merge(partyService.GetPartiesById(request.PartyUuids, cancellationToken));
        }

        if (request.OrganizationIdentifiers is { Count: > 0 })
        {
            parties = parties.Merge(partyService.LookupPartiesBySSNOrOrgNos(request.OrganizationIdentifiers.Select(static org => org.ToString()), cancellationToken));
        }

        return ValueTask.FromResult(
            new Result<IAsyncEnumerable<PartyIdentifiers>>(
                parties
                    .DistinctBy(static party => party.PartyId)
                    .Select(static party => PartyIdentifiers.Create(party))));
    }
}

/// <summary>
/// Get party identifiers from the local A3 database.
/// </summary>
internal sealed class GetV1PartyIdentifiersFromDBRequestHandler(IUnitOfWorkManager manager)
    : IRequestHandler<GetV1PartyIdentifiersRequest, IAsyncEnumerable<PartyIdentifiers>>
{
    /// <inheritdoc/>
    public ValueTask<Result<IAsyncEnumerable<PartyIdentifiers>>> Handle(GetV1PartyIdentifiersRequest request, CancellationToken cancellationToken)
        => ValueTask.FromResult(new Result<IAsyncEnumerable<PartyIdentifiers>>(GetPartyIdentifiers(request, cancellationToken)));

    private async IAsyncEnumerable<PartyIdentifiers> GetPartyIdentifiers(
        GetV1PartyIdentifiersRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var uow = await manager.CreateAsync(cancellationToken, activityName: "get v1 party identifiers");
        var persistence = uow.GetPartyPersistence();

        await foreach (var party in persistence.LookupParties(
            partyUuids: request.PartyUuids,
            partyIds: request.PartyIds,
            organizationIdentifiers: request.OrganizationIdentifiers,
            include: PartyFieldIncludes.Party | PartyFieldIncludes.Person | PartyFieldIncludes.Organization,
            cancellationToken: cancellationToken))
        {
            yield return PartyIdentifiers.Create(party);
        }
    }
}
