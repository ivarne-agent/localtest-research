using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts;
using Altinn.Register.Core.A2;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Mediator;
using Altinn.Register.Core.UnitOfWork;

namespace Altinn.Register.Core.Operations;

#pragma warning disable SA1649 // File name should match first type name

/// <summary>
/// Request to get organization information based on a <see cref="Contracts.OrganizationIdentifier"/>.
/// </summary>
/// <param name="OrganizationIdentifier">The <see cref="Contracts.OrganizationIdentifier"/>.</param>
public record GetV1OrganizationRequest(OrganizationIdentifier OrganizationIdentifier)
    : IRequest<Contracts.V1.Organization>;

/// <summary>
/// Get organization information from A2.
/// </summary>
internal sealed class GetOrganizationFromA2RequestHandler(IOrganizationClient client)
    : IRequestHandler<GetV1OrganizationRequest, Contracts.V1.Organization>
{
    /// <inheritdoc/>
    public async ValueTask<Result<Contracts.V1.Organization>> Handle(GetV1OrganizationRequest request, CancellationToken cancellationToken)
    {
        var org = await client.GetOrganization(request.OrganizationIdentifier, cancellationToken);
        if (org is null)
        {
            return Problems.OrganizationNotFound.Create([
                new("organizationIdentifier", request.OrganizationIdentifier.ToString()),
            ]);
        }

        return org;
    }
}

/// <summary>
/// Get organization information from the local A3 database.
/// </summary>
internal sealed class GetOrganizationFromDBRequestHandler(IUnitOfWorkManager manager)
    : IRequestHandler<GetV1OrganizationRequest, Contracts.V1.Organization>
{
    /// <inheritdoc/>
    public async ValueTask<Result<Contracts.V1.Organization>> Handle(GetV1OrganizationRequest request, CancellationToken cancellationToken)
    {
        await using var uow = await manager.CreateAsync(cancellationToken, activityName: "get organization");
        var persistence = uow.GetPartyPersistence();

        var org = await persistence
            .GetOrganizationByIdentifier(request.OrganizationIdentifier, include: Parties.PartyFieldIncludes.Party | Parties.PartyFieldIncludes.Organization, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        if (org is null)
        {
            return Problems.OrganizationNotFound.Create([
                new("organizationIdentifier", request.OrganizationIdentifier.ToString()),
            ]);
        }

        return V1PartyMapper.ToV1Organization(org);
    }
}
