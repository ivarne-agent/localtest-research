#nullable enable
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Authorization.Api.Contracts.AccessManagement;
using Altinn.Platform.Authorization.Localtest.Pip;
using Altinn.Platform.Authorization.Localtest.Register;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Authorization.Localtest.Controllers;

/// <summary>
/// Emulates the Access Management end-user API app-lib calls for the party list
/// (<c>AuthorizationClient.GetPartyList</c>): <c>GET accessmanagement/api/v1/enduser/authorizedparties</c>, answered
/// from the register seed (see <see cref="LocaltestAuthorizedParties"/>). The user is the token's
/// <c>urn:altinn:userid</c> claim (bearer or the runtime cookie); without a token <c>?userId=</c> is accepted, for tools.
/// <c>includeRoles</c> and <c>includeAccessPackages</c> (default true) empty the respective lists; the other
/// production flags are accepted and ignored (there are no resources, instances or delegations locally).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("accessmanagement/api/v1/enduser/authorizedparties")]
public sealed class LocaltestAuthorizedPartiesController(RegisterLocaltestClient register) : ControllerBase
{
    [HttpGet]
    [Produces("application/json")]
    public async Task<ActionResult<AuthorizedPartiesResponse>> Get(
        [FromQuery] int? userId = null,
        [FromQuery] bool includeRoles = true,
        [FromQuery] bool includeAccessPackages = true,
        CancellationToken cancellationToken = default)
    {
        int? effectiveUserId = int.TryParse(User.FindFirstValue("urn:altinn:userid"), out var claimed) ? claimed : userId;
        if (effectiveUserId is null)
        {
            return Unauthorized();
        }

        if (userId is not null && userId != effectiveUserId)
        {
            return Forbid();
        }

        var snapshot = await register.Get(cancellationToken);
        if (snapshot.UserById(effectiveUserId.Value) is not { } user)
        {
            return Ok(new AuthorizedPartiesResponse(new Links(null), []));
        }

        return Ok(new AuthorizedPartiesResponse(new Links(null), LocaltestAuthorizedParties.For(snapshot, user, includeRoles, includeAccessPackages)));
    }

    /// <summary>The paginated envelope Access Management wraps the list in; app-lib reads <c>data</c>.</summary>
    public sealed record AuthorizedPartiesResponse(Links Links, List<AuthorizedPartyDto> Data);

    public sealed record Links(string? Next);
}
