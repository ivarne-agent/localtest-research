using Altinn.Register.Models;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Register.Controllers;

/// <summary>
/// Controller for organization contact point API endpoints for internal consumption (e.g. Notifications) requiring neither authenticated user token nor access token authorization.
/// </summary>
/// <remarks>No longer in use.</remarks>
[ApiController]
[ApiVersion(1.0)]
[Route("register/api/v{version:apiVersion}/organizations/contactpoint")]
[ApiExplorerSettings(IgnoreApi = true)]
[Consumes("application/json")]
[Produces("application/json")]
public class OrgContactPointController : ControllerBase
{
    /// <summary>
    /// Endpoint looking up the contact points for the orgs connected to the provided organization numbers in the request body
    /// </summary>
    /// <returns>Returns an overview of the contact points for all orgs</returns>
    [HttpPost("lookup")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> PostLookup(
        [FromBody] OrgContactPointLookup orgContactPointLookup,
        CancellationToken cancellationToken = default)
        => StatusCode(StatusCodes.Status410Gone);
}
