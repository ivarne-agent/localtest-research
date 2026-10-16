using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Core.Mediator;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.UnitOfWork;
using V1Models = Altinn.Register.Contracts.V1;

namespace Altinn.Register.Core.Operations;

#pragma warning disable SA1649 // File name should match first type name

/// <summary>
/// Request to get party information based on a list of party UUIDs.
/// </summary>
/// <param name="PartyUuids">The party UUIDs.</param>
/// <param name="FetchSubUnits">Whether direct child units should be included for organizations.</param>
public readonly record struct GetV1PartyListByUuidRequest(
    IReadOnlyList<Guid> PartyUuids,
    bool FetchSubUnits)
    : IRequest<IAsyncEnumerable<V1Models.Party>>;

/// <summary>
/// Get party information from A2.
/// </summary>
internal sealed class GetV1PartyListByUuidFromA2RequestHandler(IV1PartyService partyService)
    : IRequestHandler<GetV1PartyListByUuidRequest, IAsyncEnumerable<V1Models.Party>>
{
    /// <inheritdoc/>
    public ValueTask<Result<IAsyncEnumerable<V1Models.Party>>> Handle(GetV1PartyListByUuidRequest request, CancellationToken cancellationToken)
        => ValueTask.FromResult(new Result<IAsyncEnumerable<V1Models.Party>>(
            partyService.GetPartiesById(request.PartyUuids, request.FetchSubUnits, cancellationToken)));
}

/// <summary>
/// Get party information from the local A3 database.
/// </summary>
internal sealed class GetV1PartyListByUuidFromDBRequestHandler(IUnitOfWorkManager manager)
    : IRequestHandler<GetV1PartyListByUuidRequest, IAsyncEnumerable<V1Models.Party>>
{
    /// <inheritdoc/>
    public ValueTask<Result<IAsyncEnumerable<V1Models.Party>>> Handle(GetV1PartyListByUuidRequest request, CancellationToken cancellationToken)
        => ValueTask.FromResult(new Result<IAsyncEnumerable<V1Models.Party>>(GetParties(request, cancellationToken)));

    private async IAsyncEnumerable<V1Models.Party> GetParties(
        GetV1PartyListByUuidRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var uow = await manager.CreateAsync(cancellationToken, activityName: "get v1 party list by uuid");
        var persistence = uow.GetPartyPersistence();

        var partyUuids = request.PartyUuids
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (partyUuids.Length == 0)
        {
            yield break;
        }

        var include = PartyFieldIncludes.Party | PartyFieldIncludes.Person | PartyFieldIncludes.Organization | PartyFieldIncludes.User;
        var transforms = PartyListTransforms.None;
        if (request.FetchSubUnits)
        {
            transforms |= PartyListTransforms.IncludeSubUnits;
        }

        await foreach (var party in V1PartyMapper.ToV1PartyList(
            persistence.LookupParties(partyUuids: partyUuids, include: include, transforms: transforms, cancellationToken: cancellationToken),
            cancellationToken))
        {
            yield return party;
        }
    }
}
