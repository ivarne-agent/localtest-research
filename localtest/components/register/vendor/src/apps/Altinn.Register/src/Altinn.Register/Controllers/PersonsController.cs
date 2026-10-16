#nullable enable

using System.Diagnostics;
using System.Security.Claims;
using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Mediator;
using Altinn.Register.Core.Operations;
using Altinn.Register.Models;
using AltinnCore.Authentication.Constants;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Register.Controllers;

/// <summary>
/// The <see cref="PersonsController"/> provides the API endpoints related to persons.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Authorize(Policy = "PlatformAccess")]
[Authorize(Policy = "AuthorizationLevel2")]
[Route("register/api/v{version:apiVersion}/persons")]
public class PersonsController
    : ControllerBase
{
    /// <summary>
    /// Gets the <see cref="Contracts.V1.Person"/> with the given national identity number.
    /// </summary>
    /// <remarks>
    /// This endpoint can be used to retrieve the person object for an identified person. The service
    /// will track the number of failed lookup attempts and block further requests if the number of failed
    /// lookups have exceeded a configurable number. The user will be prevented from performing new searches
    /// for a configurable number of seconds.
    /// </remarks>
    /// <returns>The party of the identified person.</returns>
    [HttpGet]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(200)]
    [Produces("application/json")]
    public async Task<ActionResult<Contracts.V1.Person>> GetPerson(
        [FromServices] IRequestSender<GetV1PersonRequest, Contracts.V1.Person> sender,
        PersonLookupIdentifiers personLookup,
        CancellationToken cancellationToken = default)
    {
        PersonIdentifier? personIdentifier = default;
        ValidationProblemBuilder builder = default;

        if (string.IsNullOrWhiteSpace(personLookup.NationalIdentityNumber))
        {
            builder.Add(StdValidationErrors.Required, $"$HEADER/{PersonLookupIdentifiers.NationalIdentityNumberHeaderName}");
        }
        else if (!PersonIdentifier.TryParse(personLookup.NationalIdentityNumber, provider: null, out personIdentifier))
        {
            builder.Add(ValidationErrors.InvalidPersonNumber, $"$HEADER/{PersonLookupIdentifiers.NationalIdentityNumberHeaderName}");
        }

        if (string.IsNullOrWhiteSpace(personLookup.LastName))
        {
            builder.Add(StdValidationErrors.Required, $"$HEADER/{PersonLookupIdentifiers.LastNameHeaderName}");
        }

        if (builder.TryBuild(out var error))
        {
            return error.ToActionResult();
        }

        Debug.Assert(personIdentifier is not null);
        Debug.Assert(personLookup.LastName is not null);
        Guid? userId = GetPartyUuid(HttpContext);
        if (userId is null)
        {
            return Forbid();
        }

        var request = new GetV1PersonRequest(
            personIdentifier,
            personLookup.LastName,
            userId.Value);
        var result = await sender.Send(request, cancellationToken);

        if (result.IsProblem)
        {
            return result.Problem.ToActionResult();
        }

        return result.Value;
    }

    private static Guid? GetPartyUuid(HttpContext context)
    {
        Claim? userIdClaim = context.User.Claims.FirstOrDefault(c => c.Type.Equals(AltinnCoreClaimTypes.PartyUUID));

        return userIdClaim is not null && Guid.TryParse(userIdClaim.Value, out Guid partyUuid)
            ? partyUuid
            : null;
    }
}
