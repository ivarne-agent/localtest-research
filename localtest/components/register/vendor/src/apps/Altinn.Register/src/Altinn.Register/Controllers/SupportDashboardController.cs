using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Register.Controllers;

/// <summary>
/// Temporary Altinn Support Dashboard API endpoints.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Authorize(Policy = "InternalOrPlatformAccess")]
[Route("register/api/v{version:apiVersion}/support-dashboard/parties")]
public class SupportDashboardController(V2.PartyController inner)
    : ControllerBase
{
    /// <summary>
    /// Gets a single party by its UUID.
    /// </summary>
    /// <param name="uuid">The party UUID.</param>
    /// <param name="fields">The fields to include in the response.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="PartyRecord"/>.</returns>
    [HttpGet("{uuid:guid}")]
    [ProducesResponseType<Party>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<Party>> GetPartyByUuid(
        [FromRoute] Guid uuid,
        [FromQuery(Name = "fields")] PartyFieldIncludes fields = PartyFieldIncludes.Identifiers | PartyFieldIncludes.PartyDisplayName,
        CancellationToken cancellationToken = default)
        => inner.GetPartyByUuid(uuid, fields, cancellationToken);
}
