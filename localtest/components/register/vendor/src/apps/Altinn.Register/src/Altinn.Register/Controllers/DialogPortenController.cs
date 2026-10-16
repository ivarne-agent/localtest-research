using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Models;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Register.Controllers;

/// <summary>
/// Temporary dialogporten API endpoints.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Authorize(Policy = "InternalOrPlatformAccess")]
[Route("register/api/v{version:apiVersion}/dialogporten/parties")]
public class DialogPortenController(V2.PartyController inner)
    : ControllerBase
{
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

    /// <summary>
    /// Sets the username for a party.
    /// </summary>
    /// <param name="request">The request body.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    [HttpPost("set-username")]
    public Task<ActionResult> SetUsername(
        [FromBody] SetUsernameRequest request,
        CancellationToken cancellationToken = default)
        => inner.SetUsername(request, cancellationToken);
}
