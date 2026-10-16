using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Models;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Register.Controllers;

/// <summary>
/// Temporary access-management API endpoints.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Authorize(Policy = "InternalOrPlatformAccess")]
[Route("register/api/v{version:apiVersion}/access-management/parties")]
public class AccessManagementController(V2.PartyController inner)
    : ControllerBase
{
    /// <summary>
    /// Gets a single party by its UUID.
    /// </summary>
    /// <param name="uuid">The party UUID.</param>
    /// <param name="fields">The fields to include in the response.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="Party"/>.</returns>
    [HttpGet("{uuid:guid}")]
    [ProducesResponseType<Party>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<Party>> GetPartyByUuid(
        [FromRoute] Guid uuid,
        [FromQuery(Name = "fields")] PartyFieldIncludes fields = PartyFieldIncludes.Identifiers | PartyFieldIncludes.PartyDisplayName,
        CancellationToken cancellationToken = default)
        => inner.GetPartyByUuid(uuid, fields, cancellationToken);

    /// <summary>
    /// Looks up parties based on the provided identifiers.
    /// </summary>
    /// <param name="parties">The party identifiers to look up.</param>
    /// <param name="fields">The fields to include in the response.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A set of parties matching the provided identifiers.</returns>
    /// <remarks>
    /// <list type="bullet">
    ///     <item>If a identifier is not found, a successful response (with fewer items) is still returned.</item>
    ///     <item>
    ///     The "type" part of the party UUID URNs are ignored, this means that if a request for <c>altinn:person:uuid:SOME_UUID</c>
    ///     is sent, but <c>SOME_UUID</c> turns out to be a organization UUID, the response will still contain the organization.
    ///     </item>
    /// </list>
    /// </remarks>
    [HttpPost("query")]
    [ProducesResponseType<ListObject<PartyRecord>>(200)]
    [ProducesResponseType<ListObject<PartyRecord>>(204)]
    [ProducesResponseType<ListObject<PartyRecord>>(206)]
    public Task<ActionResult<ListObject<Party>>> Query(
        [FromBody] ListObject<PartyUrn> parties,
        [FromQuery(Name = "fields")] PartyFieldIncludes fields = PartyFieldIncludes.Identifiers | PartyFieldIncludes.PartyDisplayName,
        CancellationToken cancellationToken = default)
        => inner.Query(parties, fields, cancellationToken);
}
