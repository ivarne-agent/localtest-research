using System.Diagnostics;
using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Mediator;
using Altinn.Register.Core.Operations;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Mapping;
using Altinn.Register.Models;
using Asp.Versioning;
using CommunityToolkit.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Register.Controllers.V2;

/// <summary>
/// Provisioning endpoints for self-identified users.
/// </summary>
/// <remarks>
/// Permanent register endpoint introduced ahead of the SBL Bridge decommission
/// (deadline 2026-06-19). Iteration 1 proxies to SBL Bridge; iteration 2 will write
/// directly to the register database. See issue #863.
/// </remarks>
[ApiController]
[ApiVersion(2.0)]
[Authorize(Policy = "PlatformAccess")]
[Route("register/api/v{version:apiVersion}/internal/parties/self-identified")]
public sealed class SelfIdentifiedUsersController
    : ControllerBase
{
    /// <summary>
    /// Gets the existing self-identified user for the supplied identity, or creates one if none exists.
    /// </summary>
    /// <param name="sender">The mediator request sender.</param>
    /// <param name="accessManagementClient">The access management client.</param>
    /// <param name="request">The request body.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The self-identified user.</returns>
    [HttpPost("")]
    [ProducesResponseType<SelfIdentifiedUser>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SelfIdentifiedUser>> GetOrCreateSelfIdentifiedUser(
        [FromServices] IRequestSender<GetOrCreateSelfIdentifiedUserRequest, NewOrExisting<SelfIdentifiedUserRecord>> sender,
        [FromServices] TempWorkarounds.AccessManagementClient accessManagementClient,
        [FromBody] SelfIdentifiedUserCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest();
        }

        ValidationProblemBuilder builder = default;

        if (string.IsNullOrWhiteSpace(request.ExternalIdentity))
        {
            builder.Add(StdValidationErrors.Required, "/externalIdentity");
        }

        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            builder.Add(StdValidationErrors.Required, "/userName");
        }

        if (request.SelfIdentifiedUserType == SelfIdentifiedUserType.IdPortenEmail
            && string.IsNullOrWhiteSpace(request.Email))
        {
            builder.Add(StdValidationErrors.Required, "/email");
        }

        if (request.SelfIdentifiedUserType is not (SelfIdentifiedUserType.IdPortenEmail or SelfIdentifiedUserType.Educational))
        {
            builder.Add(ValidationErrors.InvalidValue, "/selfIdentifiedUserType", detail: "Only 'IdPortenEmail' and 'Educational' are supported.");
        }

        if (builder.TryToActionResult(out var errorResult))
        {
            return errorResult;
        }

        Debug.Assert(request.ExternalIdentity is not null);
        Debug.Assert(request.UserName is not null);
        GetOrCreateSelfIdentifiedUserRequest mediatorRequest =
            request.SelfIdentifiedUserType switch
            {
                SelfIdentifiedUserType.IdPortenEmail => GetOrCreateSelfIdentifiedUserRequest.Email(request.Email!),
                SelfIdentifiedUserType.Educational => GetOrCreateSelfIdentifiedUserRequest.Educational(request.ExternalIdentity, request.UserName),
                _ => ThrowHelper.Unreachable<GetOrCreateSelfIdentifiedUserRequest>("should be guarded against"),
            };

        var result = await sender.Send(mediatorRequest, cancellationToken);
        if (result.IsProblem)
        {
            return result.Problem.ToActionResult();
        }

        if (result.Value.IsNew)
        {
            var displayName = result.Value.Value.DisplayName;
            Debug.Assert(displayName.HasValue);

            // ignore errors
            _ = await accessManagementClient.CreateSelfIdentifiedUser(
                partyUuid: result.Value.Value.PartyUuid.Value,
                partyId: result.Value.Value.PartyId.Value,
                userId: result.Value.Value.UserIds.Value!.CurrentValue.Value,
                type: result.Value.Value.SelfIdentifiedUserType.Value,
                displayName: displayName.Value,
                emailIdentifier: result.Value.Value.Email.Value,
                externalUrn: result.Value.Value.ExternalUrn.Value,
                cancellationToken: cancellationToken);
        }

        return Ok(result.Value.Value.ToPlatformModel());
    }
}
