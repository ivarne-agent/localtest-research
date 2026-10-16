using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Mediator;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.UnitOfWork;
using V1Models = Altinn.Register.Contracts.V1;

namespace Altinn.Register.Core.Operations;

#pragma warning disable SA1649 // File name should match first type name

/// <summary>
/// Request to get party information based on a party id.
/// </summary>
/// <param name="PartyId">The party id.</param>
public readonly record struct GetV1PartyByIdRequest(uint PartyId)
    : IRequest<V1Models.Party>;

/// <summary>
/// Get party information from A2.
/// </summary>
internal sealed class GetV1PartyByIdFromA2RequestHandler(IV1PartyService partyService)
    : IRequestHandler<GetV1PartyByIdRequest, V1Models.Party>
{
    /// <inheritdoc/>
    public async ValueTask<Result<V1Models.Party>> Handle(GetV1PartyByIdRequest request, CancellationToken cancellationToken)
    {
        var party = await partyService.GetPartyById(checked((int)request.PartyId), cancellationToken);
        if (party is null)
        {
            return Problems.PartyNotFound.Create([
                new("partyId", request.PartyId.ToString()),
            ]);
        }

        return party;
    }
}

/// <summary>
/// Get party information from the local A3 database.
/// </summary>
internal sealed class GetV1PartyByIdFromDBRequestHandler(IUnitOfWorkManager manager)
    : IRequestHandler<GetV1PartyByIdRequest, V1Models.Party>
{
    /// <inheritdoc/>
    public async ValueTask<Result<V1Models.Party>> Handle(GetV1PartyByIdRequest request, CancellationToken cancellationToken)
    {
        await using var uow = await manager.CreateAsync(cancellationToken, activityName: "get v1 party");
        var persistence = uow.GetPartyPersistence();

        var party = await persistence
            .GetPartyById(request.PartyId, PartyFieldIncludes.Party | PartyFieldIncludes.Person | PartyFieldIncludes.Organization | PartyFieldIncludes.User | PartyFieldIncludes.SelfIdentifiedUser, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        if (party is null)
        {
            return Problems.PartyNotFound.Create([
                new("partyId", request.PartyId.ToString()),
            ]);
        }

        return V1PartyMapper.ToV1Party(party);
    }
}
