using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Mediator;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.UnitOfWork;
using V1Models = Altinn.Register.Contracts.V1;

namespace Altinn.Register.Core.Operations;

#pragma warning disable SA1649 // File name should match first type name

/// <summary>
/// Request to get party information based on a party UUID.
/// </summary>
/// <param name="PartyUuid">The party UUID.</param>
public readonly record struct GetV1PartyByUuidRequest(Guid PartyUuid)
    : IRequest<V1Models.Party>;

/// <summary>
/// Get party information from A2.
/// </summary>
internal sealed class GetV1PartyByUuidFromA2RequestHandler(IV1PartyService partyService)
    : IRequestHandler<GetV1PartyByUuidRequest, V1Models.Party>
{
    /// <inheritdoc/>
    public async ValueTask<Result<V1Models.Party>> Handle(GetV1PartyByUuidRequest request, CancellationToken cancellationToken)
    {
        var party = await partyService.GetPartyById(request.PartyUuid, cancellationToken);
        if (party is null)
        {
            return Problems.PartyNotFound.Create([
                new("partyUuid", request.PartyUuid.ToString()),
            ]);
        }

        return party;
    }
}

/// <summary>
/// Get party information from the local A3 database.
/// </summary>
internal sealed class GetV1PartyByUuidFromDBRequestHandler(IUnitOfWorkManager manager)
    : IRequestHandler<GetV1PartyByUuidRequest, V1Models.Party>
{
    /// <inheritdoc/>
    public async ValueTask<Result<V1Models.Party>> Handle(GetV1PartyByUuidRequest request, CancellationToken cancellationToken)
    {
        await using var uow = await manager.CreateAsync(cancellationToken, activityName: "get v1 party");
        var persistence = uow.GetPartyPersistence();

        var party = await persistence
            .GetPartyById(request.PartyUuid, PartyFieldIncludes.Party | PartyFieldIncludes.Person | PartyFieldIncludes.Organization | PartyFieldIncludes.User | PartyFieldIncludes.SelfIdentifiedUser, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        if (party is null)
        {
            return Problems.PartyNotFound.Create([
                new("partyUuid", request.PartyUuid.ToString()),
            ]);
        }

        return V1PartyMapper.ToV1Party(party);
    }
}
