using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Mediator;
using Altinn.Register.Core.Operations;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Register.Controllers;

/// <summary>
/// The organizations controller provides access to organization information in the SBL Register component.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Authorize(Policy = "InternalOrPlatformAccess")]
[Route("register/api/v{version:apiVersion}/organizations")]
public class OrganizationsController
    : ControllerBase
{
    /// <summary>
    /// Gets the organization information for a given organization number.
    /// </summary>
    /// <param name="sender">The request sender.</param>
    /// <param name="orgNr">The organization number to retrieve information about.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The information about a given organization.</returns>
    [HttpGet("{orgNr}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Produces("application/json")]
    public async Task<ActionResult<Contracts.V1.Organization>> Get(
        [FromServices] IRequestSender<GetV1OrganizationRequest, Contracts.V1.Organization> sender,
        string orgNr,
        CancellationToken cancellationToken = default)
    {
        if (!OrganizationIdentifier.TryParse(orgNr, provider: null, out var organizationIdentifier))
        {
            return ValidationErrors.InvalidOrganizationNumber
                .Create("/$PATH/orgNr")
                .ToProblemInstance()
                .ToActionResult();
        }

        var result = await sender.Send(new GetV1OrganizationRequest(organizationIdentifier), cancellationToken);
        if (result.IsProblem)
        {
            return result.Problem.ToActionResult();
        }

        return Ok(result.Value);
    }
}
