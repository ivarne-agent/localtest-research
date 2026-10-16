using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ModelUtils.AspNet;
using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts;
using Altinn.Register.Contracts.ExternalRoles;
using Altinn.Register.Core;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.Core.Utils;
using Altinn.Register.Mapping;
using Altinn.Register.ModelBinding;
using Altinn.Register.Models;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Controllers.V2;

/// <summary>
/// Provides access to parties in the register.
/// </summary>
[ApiController]
[ApiVersion(2.0)]
[Authorize(Policy = "InternalOrPlatformAccess")]
[Route("register/api/v{version:apiVersion}/internal/parties")]
public partial class PartyController
    : ControllerBase
{
    private const PartyFieldIncludes REQUIRED_FIELDS =
          PartyFieldIncludes.PartyUuid
        | PartyFieldIncludes.PartyType
        | PartyFieldIncludes.PartyOrganizationIdentifier
        | PartyFieldIncludes.PartyPersonIdentifier
        | PartyFieldIncludes.PartyVersionId;

    /// <summary>
    /// The maximum number of items that can be queried at once.
    /// </summary>
    /// <remarks>
    /// Increasing this number is *not* a breaking change.
    /// Decreasing this number *is* a breaking change.
    /// </remarks>
    internal const int PARTY_QUERY_MAX_ITEMS = 100;

    /// <summary>
    /// Route name for <see cref="GetStream(PartyFieldIncludes, FlagsEnum{PartyTypes}, Opaque{ulong}?, CancellationToken)"/>.
    /// </summary>
    public const string ROUTE_GET_STREAM = "parties/stream";

    /// <summary>
    /// Route name for <see cref="GetExternalRoleAssignmentsStream(Opaque{ulong}?, CancellationToken)"/>.
    /// </summary>
    public const string ROUTE_GET_EXTERNALROLE_ASSIGNMENTS_STREAM = "external-roles/assignments/stream";

    private readonly IUnitOfWorkManager _uowManager;
    private readonly TimeProvider _timeProvider;
    private readonly Settings _settings;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartyController"/> class.
    /// </summary>
    public PartyController(
        IUnitOfWorkManager uowManager,
        IOptions<Settings> settings,
        TimeProvider timeProvider,
        IConfiguration configuration)
    {
        _uowManager = uowManager;
        _settings = settings.Value;
        _configuration = configuration;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Gets a stream of parties.
    /// </summary>
    /// <param name="fields">What fields to include.</param>
    /// <param name="partyTypesInput">Filter by party types.</param>
    /// <param name="token">An optional continuation token.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A stream of all party records.</returns>
    /// <remarks>
    /// The page-size for the stream is not constant, and can change over time. It should not be relied upon.
    /// </remarks>
    [HttpGet("stream", Name = ROUTE_GET_STREAM)]
    public async Task<ActionResult<ItemStream<Party>>> GetStream(
        [FromQuery(Name = "fields")] PartyFieldIncludes fields = PartyFieldIncludes.Identifiers | PartyFieldIncludes.PartyDisplayName,
        [FromQuery(Name = "types")] FlagsEnum<PartyTypes> partyTypesInput = default,
        [FromQuery(Name = "token")] Opaque<ulong>? token = null,
        CancellationToken cancellationToken = default)
    {
        var pageSize = _settings.PartyStreamPageSize;
        IReadOnlySet<PartyRecordType>? partyTypes = partyTypesInput.Value == PartyTypes.None ? null : new PartyTypesSet(partyTypesInput.Value);

        await using var uow = await _uowManager.CreateAsync(cancellationToken);
        var persistence = uow.GetPartyPersistence();

        var maxVersionId = await persistence.GetMaxPartyVersionId(cancellationToken);
        var parties = await persistence.GetPartyStream(
            fromExclusive: token?.Value ?? 0,
            limit: pageSize,
            include: fields | REQUIRED_FIELDS,
            filterByPartyType: partyTypes,
            cancellationToken)
            .Select(static p => p.ToPlatformModel())
            .ToListAsync(cancellationToken);

        string? nextLink = null;
        if (parties.Count > 0)
        {
            var routeValues = new RouteValueDictionary()
            {
                { "token", Opaque.Create(parties[^1].VersionId) },
                { "fields", PartyFieldIncludesModelBinder.Format(fields) },
            };

            if (partyTypes != null)
            {
                routeValues.Add("types", partyTypesInput.ToString());
            }

            nextLink = Url.Link(ROUTE_GET_STREAM, routeValues);
        }

        return ItemStream.Create(
            parties,
            next: nextLink,
            sequenceMax: maxVersionId,
            sequenceNumberFactory: static p => p.VersionId);
    }

    /// <summary>
    /// Gets a stream of external-role assignments.
    /// </summary>
    /// <param name="token">An optional continuation token.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A stream of all external-role assignment events.</returns>
    [HttpGet("external-roles/assignments/events/stream", Name = ROUTE_GET_EXTERNALROLE_ASSIGNMENTS_STREAM)]
    public async Task<ActionResult<ItemStream<ExternalRoleAssignmentEvent>>> GetExternalRoleAssignmentsStream(
        [FromQuery(Name = "token")] Opaque<ulong>? token = null,
        CancellationToken cancellationToken = default)
    {
        var pageSize = _settings.RoleAssignmentsStreamPageSize;

        await using var uow = await _uowManager.CreateAsync(cancellationToken);
        var persistence = uow.GetPartyPersistence();

        var maxVersionId = await persistence.GetMaxExternalRoleAssignmentVersionId(cancellationToken);
        var events = await persistence.GetExternalRoleAssignmentStream(
            fromExclusive: token?.Value ?? 0,
            limit: pageSize,
            cancellationToken)
            .ToListAsync(cancellationToken);

        string? nextLink = null;
        if (events.Count > 0)
        {
            nextLink = Url.Link(ROUTE_GET_EXTERNALROLE_ASSIGNMENTS_STREAM, new
            {
                token = Opaque.Create(events[^1].VersionId),
            });
        }

        return ItemStream.Create(
            events,
            next: nextLink,
            sequenceMax: maxVersionId,
            sequenceNumberFactory: static e => e.VersionId);
    }

    /// <summary>
    /// Gets all external role assignments from a party.
    ///
    /// This endpoint is not finalized, and is subject to change without a major version bump.
    /// </summary>
    /// <param name="partyUuid">The party uuid.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A list of external role assignments for the specified party.</returns>
    [HttpGet("{partyUuid:guid}/external-roles")]
    public async Task<ActionResult<ListObject<PartyExternalRoleAssignmentRecord>>> GetExternalRoleAssignmentsFromParty(
        Guid partyUuid,
        CancellationToken cancellationToken = default)
    {
        await using var uow = await _uowManager.CreateAsync(cancellationToken);
        var persistence = uow.GetPartyExternalRolePersistence();

        var assignments = await persistence.GetExternalRoleAssignmentsFromParty(
            partyUuid,
            PartyExternalRoleAssignmentFieldIncludes.RoleAssignment,
            cancellationToken)
            .ToListAsync(cancellationToken);

        return ListObject.Create(assignments);
    }

    private static readonly ExternalRoleReference _revisorRole = new(ExternalRoleSource.CentralCoordinatingRegister, "revisor");

    /// <summary>
    /// Gets all parties that have assigned the "revisor" ccr role to <paramref name="partyUuid"/>.
    /// </summary>
    /// <param name="partyUuid">The revisor party uuid.</param>
    /// <param name="fields">The party fields to include.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A set of parties that have assigned the "revisor" ccr role to <paramref name="partyUuid"/>.</returns>
    [ApiVersion(1.0)]
    [ApiVersion(2.0)]
    [HttpGet("{partyUuid:guid}/customers/ccr/revisor")]
    [ProducesResponseType<ListObject<Party>>(StatusCodes.Status200OK)]
    public Task<ActionResult<ListObject<Party>>> GetRevisorCustomers(
        [FromRoute] Guid partyUuid,
        [FromQuery(Name = "fields")] PartyFieldIncludes fields = PartyFieldIncludes.Identifiers | PartyFieldIncludes.PartyDisplayName,
        CancellationToken cancellationToken = default)
        => GetCustomers(partyUuid, _revisorRole, fields, cancellationToken);

    private static readonly ExternalRoleReference _regnskapsforerRole = new(ExternalRoleSource.CentralCoordinatingRegister, "regnskapsforer");

    /// <summary>
    /// Gets all parties that have assigned the "regnskapsforer" ccr role to <paramref name="partyUuid"/>.
    /// </summary>
    /// <param name="partyUuid">The revisor party uuid.</param>
    /// <param name="fields">The party fields to include.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A set of parties that have assigned the "regnskapsforer" ccr role to <paramref name="partyUuid"/>.</returns>
    [ApiVersion(1.0)]
    [ApiVersion(2.0)]
    [HttpGet("{partyUuid:guid}/customers/ccr/regnskapsforer")]
    [ProducesResponseType<ListObject<Party>>(StatusCodes.Status200OK)]
    public Task<ActionResult<ListObject<Party>>> GetRegnskapsforerCustomers(
        [FromRoute] Guid partyUuid,
        [FromQuery(Name = "fields")] PartyFieldIncludes fields = PartyFieldIncludes.Identifiers | PartyFieldIncludes.PartyDisplayName,
        CancellationToken cancellationToken = default)
        => GetCustomers(partyUuid, _regnskapsforerRole, fields, cancellationToken);

    private static readonly ExternalRoleReference _forretningsforerRole = new(ExternalRoleSource.CentralCoordinatingRegister, "forretningsforer");

    /// <summary>
    /// Gets all parties that have assigned the "forretningsforer" ccr role to <paramref name="partyUuid"/>.
    /// </summary>
    /// <param name="partyUuid">The revisor party uuid.</param>
    /// <param name="fields">The party fields to include.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A set of parties that have assigned the "forretningsforer" ccr role to <paramref name="partyUuid"/>.</returns>
    [ApiVersion(1.0)]
    [ApiVersion(2.0)]
    [HttpGet("{partyUuid:guid}/customers/ccr/forretningsforer")]
    [ProducesResponseType<ListObject<Party>>(StatusCodes.Status200OK)]
    public Task<ActionResult<ListObject<Party>>> GetForretningsforerCustomers(
        [FromRoute] Guid partyUuid,
        [FromQuery(Name = "fields")] PartyFieldIncludes fields = PartyFieldIncludes.Identifiers | PartyFieldIncludes.PartyDisplayName,
        CancellationToken cancellationToken = default)
        => GetCustomers(partyUuid, _forretningsforerRole, fields, cancellationToken);

    [NonAction]
    private async Task<ActionResult<ListObject<Party>>> GetCustomers(
        Guid partyUuid,
        ExternalRoleReference role,
        PartyFieldIncludes fields,
        CancellationToken cancellationToken)
    {
        await using var uow = await _uowManager.CreateAsync(cancellationToken);
        var partyPersistence = uow.GetPartyPersistence();
        var rolePersistence = uow.GetPartyExternalRolePersistence();

        List<Guid> customerPartyUuids;

        {
            using var activity = RegisterTelemetry.StartActivity("GetExternalRoleAssignmentsToParty");

            customerPartyUuids = await rolePersistence.GetExternalRoleAssignmentsToParty(
                partyUuid,
                role,
                PartyExternalRoleAssignmentFieldIncludes.RoleFromParty,
                cancellationToken)
                .Select(static r => r.FromParty.Value)
                .ToListAsync(cancellationToken);
        }

        if (customerPartyUuids.Count == 0)
        {
            return StatusCode(StatusCodes.Status200OK, ListObject.Create<Party>([]));
        }

        List<Party> customers;

        {
            using var activity = RegisterTelemetry.StartActivity("LookupParties");

            customers = await partyPersistence.LookupParties(
                partyUuids: customerPartyUuids,
                include: fields | REQUIRED_FIELDS,
                cancellationToken: cancellationToken)
                .Select(static p => p.ToPlatformModel())
                .ToListAsync(cancellationToken);
        }

        return ListObject.Create(customers);
    }

    private static readonly ExternalRoleReference _dagligLederRole = new(ExternalRoleSource.CentralCoordinatingRegister, "daglig-leder");

    /// <summary>
    /// Gets all parties assigned the "daglig-leder" ccr role by <paramref name="partyUuid"/>.
    /// </summary>
    /// <param name="partyUuid">The party uuid to find "daglig-leder"s for.</param>
    /// <param name="fields">The party fields to include.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A set of parties that have been assigned the "daglig-leder" ccr role from <paramref name="partyUuid"/>.</returns>
    [ApiVersion(1.0)]
    [ApiVersion(2.0)]
    [HttpGet("{partyUuid:guid}/holders/ccr/daglig-leder")]
    [ProducesResponseType<ListObject<Party>>(StatusCodes.Status200OK)]
    public Task<ActionResult<ListObject<Party>>> GetDagligLedere(
        [FromRoute] Guid partyUuid,
        [FromQuery(Name = "fields")] PartyFieldIncludes fields = PartyFieldIncludes.Identifiers | PartyFieldIncludes.PartyDisplayName,
        CancellationToken cancellationToken = default)
        => GetRoleHolders(partyUuid, _dagligLederRole, fields, cancellationToken);

    [NonAction]
    private async Task<ActionResult<ListObject<Party>>> GetRoleHolders(
        Guid partyUuid,
        ExternalRoleReference role,
        PartyFieldIncludes fields,
        CancellationToken cancellationToken)
    {
        await using var uow = await _uowManager.CreateAsync(cancellationToken);
        var partyPersistence = uow.GetPartyPersistence();
        var rolePersistence = uow.GetPartyExternalRolePersistence();

        List<Guid> customerPartyUuids;

        {
            using var activity = RegisterTelemetry.StartActivity("GetExternalRoleAssignmentsFromParty");

            customerPartyUuids = await rolePersistence.GetExternalRoleAssignmentsFromParty(
                partyUuid,
                role,
                PartyExternalRoleAssignmentFieldIncludes.RoleToParty,
                cancellationToken)
                .Select(static r => r.ToParty.Value)
                .ToListAsync(cancellationToken);
        }

        List<Party> holders;

        {
            using var activity = RegisterTelemetry.StartActivity("LookupParties");

            holders = await partyPersistence.LookupParties(
                partyUuids: customerPartyUuids,
                include: fields | REQUIRED_FIELDS,
                cancellationToken: cancellationToken)
                .Select(static p => p.ToPlatformModel())
                .ToListAsync(cancellationToken);
        }

        return ListObject.Create(holders);
    }

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
    public async Task<ActionResult<Party>> GetPartyByUuid(
        [FromRoute] Guid uuid,
        [FromQuery(Name = "fields")] PartyFieldIncludes fields = PartyFieldIncludes.Identifiers | PartyFieldIncludes.PartyDisplayName,
        CancellationToken cancellationToken = default)
    {
        await using var uow = await _uowManager.CreateAsync(cancellationToken);
        var persistence = uow.GetPartyPersistence();

        var party = await persistence.GetPartyById(uuid, fields | REQUIRED_FIELDS, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        if (party is null)
        {
            return Problems.ReferencedPartyNotFound.Create([new("partyUuid", uuid.ToString())])
                .ToActionResult();
        }

        return Ok(party.ToPlatformModel());
    }

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
    [ProducesResponseType<ListObject<Party>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ListObject<Party>>(StatusCodes.Status206PartialContent)]
    public Task<ActionResult<ListObject<Party>>> Query(
        [FromBody] ListObject<PartyUrn> parties,
        [FromQuery(Name = "fields")] PartyFieldIncludes fields = PartyFieldIncludes.Identifiers | PartyFieldIncludes.PartyDisplayName,
        CancellationToken cancellationToken = default)
        => QueryCore<Party>(parties.Items, fields, PartyListTransforms.None, cancellationToken);

    [NonAction]
    private async Task<ActionResult<ListObject<TParty>>> QueryCore<TParty>(
        IEnumerable<PartyUrn> parties,
        PartyFieldIncludes fields,
        PartyListTransforms transforms,
        CancellationToken cancellationToken)
        where TParty : Party
    {
        const int MAX_ITEMS = PARTY_QUERY_MAX_ITEMS;

        ValidationProblemBuilder errors = default;

        List<Guid>? uuids = null;
        List<uint>? ids = null;
        List<PersonIdentifier>? personIds = null;
        List<OrganizationIdentifier>? orgIds = null;
        List<uint>? userIds = null;
        List<string>? usernames = null;
        List<string>? idpEmails = null;

        var count = 0;
        foreach (var item in parties)
        {
            switch (item)
            {
                case PartyUrn.PartyId partyId:
                    fields |= PartyFieldIncludes.PartyId;
                    ids ??= new();
                    ids.Add(partyId.Value);
                    break;

                case PartyUrn.PartyUuid partyUuid:
                    fields |= PartyFieldIncludes.PartyUuid;
                    uuids ??= new();
                    uuids.Add(partyUuid.Value);
                    break;

                case PartyUrn.PersonId personId:
                    fields |= PartyFieldIncludes.PartyPersonIdentifier;
                    personIds ??= new();
                    personIds.Add(personId.Value);
                    break;

                case PartyUrn.OrganizationId orgId:
                    fields |= PartyFieldIncludes.PartyOrganizationIdentifier;
                    orgIds ??= new();
                    orgIds.Add(orgId.Value);
                    break;

                case PartyUrn.UserId userId:
                    fields |= PartyFieldIncludes.UserId;
                    userIds ??= new();
                    userIds.Add(userId.Value);
                    break;

                case PartyUrn.Username username:
                    fields |= PartyFieldIncludes.Username;
                    usernames ??= new();
                    usernames.Add(username.Value.Value);
                    break;

                case PartyUrn.IDPortenEmail idpEmail:
                    fields |= PartyFieldIncludes.SelfIdentifiedUserEmail;
                    idpEmails ??= new();
                    idpEmails.Add(idpEmail.Value.Value);
                    break;

                default:
                    errors.Add(ValidationErrors.PartyUrn_Invalid, $"/data/{count}");
                    break;
            }

            count++;
        }

        using var activity = RegisterTelemetry.StartActivity("query parties");

        if (count == 0)
        {
            activity?.AddTag("query.empty", "true");
            return StatusCode(StatusCodes.Status200OK, ListObject.Create<PartyRecord>([]));
        }

        if (count > MAX_ITEMS)
        {
            errors.Add(ValidationErrors.TooManyItems, "/data");
        }

        if (errors.TryToActionResult(out var actionResult))
        {
            activity?.AddTag("query.error", "validation");
            return actionResult;
        }

        activity?.AddTag("query.count", count);
        await using var uow = await _uowManager.CreateAsync(cancellationToken);
        var persistence = uow.GetPartyPersistence();

        var result = await persistence.LookupParties(
            partyUuids: uuids,
            partyIds: ids,
            organizationIdentifiers: orgIds,
            personIdentifiers: personIds,
            userIds: userIds,
            usernames: usernames,
            selfIdentifiedEmails: idpEmails,
            include: fields | REQUIRED_FIELDS,
            transforms: transforms,
            cancellationToken: cancellationToken)
            .Select(static p => p.ToPlatformModel())
            .Cast<TParty>()
            .ToListAsync(cancellationToken);

        // We currently only have enough information to check for "missing" if we do not transform the list
        var statusCode = StatusCodes.Status200OK;
        if (transforms is PartyListTransforms.None)
        {
            // TODO: rewrite this to give out which inputs matched which outputs
            // see https://github.com/Altinn/altinn-register/issues/659
            var missingById = ids.OrEmpty().Any(id => !result.Any(p => p.PartyId.Value == id));
            var missingByUuid = uuids.OrEmpty().Any(uuid => !result.Any(p => p.Uuid == uuid));
            var missingByOrgId = orgIds.OrEmpty().Any(orgId => !result.Any(p => p is Organization o && o.OrganizationIdentifier == orgId));
            var missingByPersonId = personIds.OrEmpty().Any(personId => !result.Any(p => p is Person pp && pp.PersonIdentifier == personId));
            var missingByUserId = userIds.OrEmpty().Any(uid => !result.Any(p => p.User.HasValue && p.User.Value.UserIds.HasValue && p.User.Value.UserIds.Value.Contains(uid)));
            var missingByUsername = usernames.OrEmpty().Any(username => !result.Any(p => p.User.HasValue && p.User.Value.Username.HasValue && string.Equals(p.User.Value.Username.Value, username, StringComparison.OrdinalIgnoreCase)));
            var missingByIdpEmail = idpEmails.OrEmpty().Any(email => !result.Any(p => p is SelfIdentifiedUser s && s.Email.HasValue && string.Equals(s.Email.Value, email, StringComparison.OrdinalIgnoreCase)));
            var anyMissing = missingById || missingByUuid || missingByOrgId || missingByPersonId || missingByUserId || missingByUsername || missingByIdpEmail;

            if (anyMissing)
            {
                // in case we requested a single item, it was not found, and the item was an organization, we log the query
                if (count == 1 && orgIds is { Count: 1 })
                {
                    // we're in an "error" case here, so we're fine with double-iterating over the parties enumerable
                    activity?.AddTag("query.urn", parties.First().ToString());
                }

                activity?.AddTag("query.missing.by-id", missingById);
                activity?.AddTag("query.missing.by-uuid", missingByUuid);
                activity?.AddTag("query.missing.by-org-id", missingByOrgId);
                activity?.AddTag("query.missing.by-person-id", missingByPersonId);
                activity?.AddTag("query.missing.by-user-id", missingByUserId);
                activity?.AddTag("query.missing.by-username", missingByUsername);
                activity?.AddTag("query.missing.by-idp-email", missingByIdpEmail);
                statusCode = StatusCodes.Status206PartialContent;
            }
        }

        activity?.AddTag("query.resultCount", result.Count);
        return StatusCode(statusCode, ListObject.Create(result));
    }

    /// <summary>
    /// Sets the username for a party.
    /// </summary>
    /// <param name="request">The request body.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    [HttpPost("set-username")]
    public async Task<ActionResult> SetUsername(
        [FromBody] SetUsernameRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!PersistenceFeatureFlag.CanCreatePartyId(_configuration))
        {
            return Problems.FeatureDisabled.ToActionResult(
                detail: "Setting of usernames is still managed by A2");
        }

        PersonIdentifier? personId = null;
        ValidationProblemBuilder errors = default;

        if (request.Username is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Username))
            {
                errors.Add(ValidationErrors.InvalidValue, path: "/username", detail: "Username cannot be empty or whitespace.");
            }
            else if (!GetValidUsernameRegex().IsMatch(request.Username))
            {
                errors.Add(ValidationErrors.InvalidValue, path: "/username", detail: "Username must be 6-64 characters long, start with a letter, and can only contain letters, digits, dots, underscores, hyphens or @ signs.");
            }
        }

        if (string.IsNullOrWhiteSpace(request.Party))
        {
            errors.Add(StdValidationErrors.Required, path: "/party");
        }

        if (!PartyExternalRefUrn.TryParse(request.Party, out var partyRef))
        {
            errors.Add(ValidationErrors.PartyUrn_Invalid, path: "/party");
        }
        else if (!partyRef.IsPersonId(out personId))
        {
            errors.Add(ValidationErrors.InvalidValue, path: "/party", detail: "Only person identifiers are allowed at this time.");
        }

        if (errors.TryToActionResult(out var actionResult))
        {
            return actionResult;
        }

        Debug.Assert(personId is not null);
        Debug.Assert(request.Username is null || !string.IsNullOrWhiteSpace(request.Username));

        await using var uow = await _uowManager.CreateAsync(activityName: "set username", cancellationToken: cancellationToken);
        var persistence = uow.GetPartyPersistence();

        var now = _timeProvider.GetUtcNow();
        var result = await persistence.UpsertParty(
            new PersonRecord
            {
                PersonIdentifier = personId,
                OrganizationIdentifier = FieldValue.Null,
                CreatedAt = now, // will not be used, but required
                ModifiedAt = now,
                Usernames =
                    request.Username switch
                    {
                        null => PartyHistoricalAggregate<string>.Empty,
                        { } username => PartyHistoricalAggregate<string>.CreateCurrent(username),
                    },

                PartyUuid = FieldValue.Unset,
                PartyId = FieldValue.Unset,
                OwnerUuid = FieldValue.Unset,
                ExternalUrn = FieldValue.Unset,
                DisplayName = FieldValue.Unset,
                UserIds = FieldValue.Unset,
                IsDeleted = FieldValue.Unset,
                DeletedAt = FieldValue.Unset,
                VersionId = FieldValue.Unset,

                Source = FieldValue.Unset,
                FirstName = FieldValue.Unset,
                MiddleName = FieldValue.Unset,
                LastName = FieldValue.Unset,
                ShortName = FieldValue.Unset,
                Address = FieldValue.Unset,
                MailingAddress = FieldValue.Unset,
                DateOfBirth = FieldValue.Unset,
                DateOfDeath = FieldValue.Unset,
            },
            cancellationToken);

        if (result.IsProblem)
        {
            if (result.Problem.ErrorCode == Problems.PartyNotFound.ErrorCode)
            {
                return Problems.ReferencedPartyNotFound.ToActionResult();
            }

            if (result.Problem.ErrorCode == Problems.PartyConflict.ErrorCode)
            {
                return Problems.UsernameInUse.ToActionResult();
            }

            return result.Problem.ToActionResult();
        }

        await uow.CommitAsync(cancellationToken);
        return Ok();
    }

    [GeneratedRegex(@"^[a-z][a-z0-9._@\-]{5,63}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex GetValidUsernameRegex();

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
    {
        // all valid organization urns **shoulde** be valid party urns
        var partyUrn = PartyUrn.Parse(request.Item.ToString());
        return QueryCore<Organization>(
            [partyUrn],
            fields,
            PartyListTransforms.ReplaceWithMainUnits,
            cancellationToken);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T Unreachable<T>()
    {
        throw new InvalidOperationException("This code should never be reached.");
    }

    /// <summary>
    /// Settings for the <see cref="PartyController"/>.
    /// </summary>
    public sealed class Settings
    {
        /// <summary>
        /// Gets or sets the page-size for party streams.
        /// </summary>
        /// <remarks>
        /// Changing this number is <b>not</b> a breaking change.
        /// </remarks>
        public ushort PartyStreamPageSize { get; set; } = 1_000;

        /// <summary>
        /// Gets or sets the page-size for role-assignments streams.
        /// </summary>
        /// <remarks>
        /// Changing this number is *not* a breaking change.
        /// </remarks>
        public ushort RoleAssignmentsStreamPageSize { get; set; } = 1_000;
    }
}
