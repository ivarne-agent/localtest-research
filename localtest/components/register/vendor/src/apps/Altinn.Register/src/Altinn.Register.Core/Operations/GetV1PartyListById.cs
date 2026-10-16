using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Core.Mediator;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.UnitOfWork;
using V1Models = Altinn.Register.Contracts.V1;

namespace Altinn.Register.Core.Operations;

#pragma warning disable SA1649 // File name should match first type name

/// <summary>
/// Request to get party information based on a list of party ids.
/// </summary>
/// <param name="PartyIds">The party ids.</param>
/// <param name="FetchSubUnits">Whether direct child units should be included for organizations.</param>
public readonly record struct GetV1PartyListByIdRequest(
    IReadOnlyList<uint> PartyIds,
    bool FetchSubUnits)
    : IRequest<IAsyncEnumerable<V1Models.Party>>;

/// <summary>
/// Get party information from A2.
/// </summary>
internal sealed class GetV1PartyListByIdFromA2RequestHandler(IV1PartyService partyService)
    : IRequestHandler<GetV1PartyListByIdRequest, IAsyncEnumerable<V1Models.Party>>
{
    /// <inheritdoc/>
    public ValueTask<Result<IAsyncEnumerable<V1Models.Party>>> Handle(GetV1PartyListByIdRequest request, CancellationToken cancellationToken)
        => ValueTask.FromResult(new Result<IAsyncEnumerable<V1Models.Party>>(
            partyService.GetPartiesById(request.PartyIds.Select(static id => checked((int)id)), request.FetchSubUnits, cancellationToken)));
}

/// <summary>
/// Get party information from the local A3 database.
/// </summary>
internal sealed class GetV1PartyListByIdFromDBRequestHandler(IUnitOfWorkManager manager)
    : IRequestHandler<GetV1PartyListByIdRequest, IAsyncEnumerable<V1Models.Party>>
{
    /// <inheritdoc/>
    public ValueTask<Result<IAsyncEnumerable<V1Models.Party>>> Handle(GetV1PartyListByIdRequest request, CancellationToken cancellationToken)
        => ValueTask.FromResult(new Result<IAsyncEnumerable<V1Models.Party>>(GetParties(request, cancellationToken)));

    private async IAsyncEnumerable<V1Models.Party> GetParties(
        GetV1PartyListByIdRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var uow = await manager.CreateAsync(cancellationToken, activityName: "get v1 party list by id");
        var persistence = uow.GetPartyPersistence();

        if (request.PartyIds.Count == 0)
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
            persistence.LookupParties(partyIds: request.PartyIds, include: include, transforms: transforms, cancellationToken: cancellationToken),
            cancellationToken))
        {
            yield return party;
        }
    }
}
