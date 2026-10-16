using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
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
[Route("register/api/v{version:apiVersion}/correspondence/parties")]
public class CorrespondenceController(IUnitOfWorkManager uowManager, V2.PartyController inner)
    : ControllerBase
{
    private static readonly FrozenSet<string> CorrespondenceRolesIdentifiers = [
        "innehaver",
        "komplementar",
        "styreleder",
        "deltaker-delt-ansvar",
        "deltaker-fullt-ansvar",
        "bestyrende-reder",
        "daglig-leder",
        "bostyrer",
        "kontaktperson-ados",
        "norsk-representant",
    ];

    /// <summary>
    /// Gets roles used by correspondence from a party.
    /// </summary>
    /// <param name="partyUuid">The party uuid.</param>
    /// <param name="token">Continuation token (used in pagination).</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A page of roles.</returns>
    [HttpGet("{partyUuid:guid}/roles/correspondence-roles")]
    public async Task<ActionResult<Paginated<ExternalRoleAssignment>>> GetCorrespondenceRoles(
        Guid partyUuid,
        [FromQuery(Name = "token")] Opaque<ulong>? token = null,
        CancellationToken cancellationToken = default)
    {
        await using var uow = await uowManager.CreateAsync(cancellationToken);
        var persistence = uow.GetPartyExternalRolePersistence();

        var fromParty = new PartyRef { Uuid = partyUuid };

        var roleRefs = new Dictionary<(ExternalRoleSource Source, string Identifier), ExternalRoleRef>();
        var assignments = new List<ExternalRoleAssignment>();
        await foreach (var assignment in persistence.GetExternalRoleAssignmentsFromParty(partyUuid, cancellationToken: cancellationToken))
        {
            Debug.Assert(assignment.Source.HasValue);
            Debug.Assert(assignment.Identifier.HasValue);
            Debug.Assert(assignment.ToParty.HasValue);
            Debug.Assert(assignment.FromParty.HasValue && assignment.FromParty.Value == partyUuid);

            var isCorrespondenceRole = assignment switch
            {
                { Source.Value: ExternalRoleSource.CentralCoordinatingRegister, Identifier.Value: { } identifier } when CorrespondenceRolesIdentifiers.Contains(identifier) => true,
                _ => false,
            };

            if (!isCorrespondenceRole)
            {
                // we only care about correspondence roles
                continue;
            }

            ref var roleDef = ref CollectionsMarshal.GetValueRefOrAddDefault(roleRefs, (assignment.Source.Value, assignment.Identifier.Value), out var exists);
            if (!exists)
            {
                roleDef = new ExternalRoleRef
                {
                    Source = assignment.Source.Value,
                    Identifier = assignment.Identifier.Value
                };
            }

            assignments.Add(new ExternalRoleAssignment
            {
                Role = roleDef!,
                FromParty = fromParty,
                ToParty = new PartyRef { Uuid = assignment.ToParty.Value },
            });
        }

        // No pagination for now
        return Paginated.Create(assignments, next: null);
    }

    /// <summary>
    /// Gets the main units of an organization (if any).
    /// </summary>
    /// <param name="request">The request body.</param>
    /// <param name="fields">The fields to include in the response.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>All organizations that are the main-units of the input organization.</returns>
    [HttpPost("main-units")]
    [ProducesResponseType<ListObject<Organization>>(StatusCodes.Status200OK)]
    public Task<ActionResult<ListObject<Organization>>> GetMainUnits(
        [FromBody] DataObject<OrganizationUrn> request,
        [FromQuery(Name = "fields")] PartyFieldIncludes fields = PartyFieldIncludes.Identifiers | PartyFieldIncludes.PartyDisplayName,
        CancellationToken cancellationToken = default)
        => inner.GetMainUnits(request, fields, cancellationToken);

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
