using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Contracts;
using Altinn.Register.Contracts.V1;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Mediator;
using Altinn.Register.Core.Operations;
using Altinn.Register.Core.Parties;
using Altinn.Register.Operations.Validation;
using Altinn.Register.Services.Interfaces;
using AltinnCore.Authentication.Constants;
using Asp.Versioning;
using CommunityToolkit.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using V1Models = Altinn.Register.Contracts.V1;

namespace Altinn.Register.Controllers;

/// <summary>
/// The parties controller provides access to party information in the SBL Register component.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Authorize(Policy = "InternalOrPlatformAccess")]
[Route("register/api/v{version:apiVersion}/parties")]
public partial class PartiesController
    : ControllerBase
{
    private const int MaxPartyNamesLookupItems = 1000;

    private readonly IAuthorizationClient _authorization;
    private readonly ILogger<PartiesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartiesController"/> class.
    /// </summary>
    public PartiesController(
        IAuthorizationClient authorizationClient,
        ILogger<PartiesController> logger)
    {
        _authorization = authorizationClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the party for a given party id.
    /// </summary>
    /// <param name="sender">The request sender.</param>
    /// <param name="partyId">The party id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The information about a given party.</returns>
    [HttpGet("{partyId:int}")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(200)]
    [Produces("application/json")]
    [Authorize]
    public async Task<ActionResult<V1Models.Party>> Get(
        [FromServices] IRequestSender<GetV1PartyByIdRequest, V1Models.Party> sender,
        int partyId,
        CancellationToken cancellationToken = default)
    {
        ValidationProblemBuilder builder = default;

        builder.TryValidate(path: "/$PATH/partyId", partyId, default(PartyIdValidator), out uint uintPartyId);

        if (builder.TryBuild(out var error))
        {
            return error.ToActionResult();
        }

        Debug.Assert(uintPartyId > 0);
        var result = await sender.Send(new GetV1PartyByIdRequest(uintPartyId), cancellationToken);
        if (result.IsProblem)
        {
            if (!IsOrg(HttpContext) && result.Problem.ErrorCode == Problems.PartyNotFound.ErrorCode)
            {
                Log.PartyNotFoundUnauthorized(_logger, partyId);
                return Unauthorized();
            }

            return result.Problem.ToActionResult();
        }

        var party = result.Value;
        if (!IsOrg(HttpContext))
        {
            if (await AuthorizePartyLookup(party, cancellationToken) is { } authorizationResult)
            {
                return authorizationResult;
            }
        }

        return Ok(party);
    }

    /// <summary>
    /// Gets the party for a given party uuid.
    /// </summary>
    /// <param name="sender">The request sender.</param>
    /// <param name="partyUuid">The party uuid.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The information about a given party.</returns>
    [HttpGet("byuuid/{partyUuid:Guid}")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(200)]
    [Produces("application/json")]
    [Authorize]
    public async Task<ActionResult<V1Models.Party>> GetByUuid(
        [FromServices] IRequestSender<GetV1PartyByUuidRequest, V1Models.Party> sender,
        [FromRoute] Guid partyUuid,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetV1PartyByUuidRequest(partyUuid), cancellationToken);
        if (result.IsProblem)
        {
            if (!IsOrg(HttpContext) && result.Problem.ErrorCode == Problems.PartyNotFound.ErrorCode)
            {
                Log.PartyNotFoundUnauthorized(_logger, partyUuid);
                return Unauthorized();
            }

            return result.Problem.ToActionResult();
        }

        var party = result.Value;
        if (!IsOrg(HttpContext))
        {
            if (await AuthorizePartyLookup(party, cancellationToken) is { } authorizationResult)
            {
                return authorizationResult;
            }
        }

        return Ok(party);
    }

    /// <summary>
    /// Perform a lookup/search for a specific party by using one of the provided ids.
    /// </summary>
    /// <param name="sender">The request sender.</param>
    /// <param name="partyLookup">The lookup criteria. One and only one of the properties must be a valid value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The identified party.</returns>
    [HttpPost("lookup")]
    [Consumes("application/json")]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(200)]
    [Produces("application/json")]
    public async Task<ActionResult<V1Models.Party>> PostPartyLookup(
        [FromServices] IRequestSender<LookupV1PartyRequest, V1Models.Party> sender,
        [FromBody] PartyLookup partyLookup,
        CancellationToken cancellationToken = default)
    {
        ValidationProblemBuilder errors = default;

        if (!errors.TryValidate(path: "/", partyLookup, default(LookupV1PartyRequestValidator), out LookupV1PartyRequest request))
        {
            if (errors.TryBuild(out var problem))
            {
                return problem.ToActionResult();
            }

            return ThrowHelper.ThrowInvalidOperationException<ActionResult<V1Models.Party>>("Validation failed without producing a problem instance.");
        }

        var result = await sender.Send(request, cancellationToken);
        if (result.IsProblem)
        {
            return result.Problem.ToActionResult();
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Perform a name lookup for the list of parties for the provided ids.
    /// </summary>
    /// <param name="sender">The request sender.</param>
    /// <param name="partyNamesLookup">A list of lookup criteria. For each criteria, one and only one of the properties must be a valid value.</param>
    /// <param name="partyComponentOption">Specifies the components that should be included when retrieving party's information.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The identified party names for the corresponding identifiers.</returns>
    [HttpPost("nameslookup")]
    [Consumes("application/json")]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(200)]
    [Produces("application/json")]
    public async Task<ActionResult<PartyNamesLookupResult>> PostPartyNamesLookup(
        [FromServices] IRequestSender<LookupV1PartyNamesRequest, PartyNamesLookupResult> sender,
        [FromBody] PartyNamesLookup partyNamesLookup,
        [FromQuery] PartyComponentOptions partyComponentOption = PartyComponentOptions.None,
        CancellationToken cancellationToken = default)
    {
        ValidationProblemBuilder errors = default;

        errors.TryValidate(
            path: "/",
            partyNamesLookup,
            new LookupV1PartyNamesRequestValidator { MaxItems = MaxPartyNamesLookupItems },
            out IReadOnlyList<LookupV1PartyRequest>? requests);

        if (errors.TryBuild(out var validationProblem))
        {
            return validationProblem.ToActionResult();
        }

        Debug.Assert(requests is not null);
        if (requests.Count == 0)
        {
            return Ok(new PartyNamesLookupResult
            {
                PartyNames = [],
            });
        }

        var request = new LookupV1PartyNamesRequest(requests, partyComponentOption);
        var result = await sender.Send(request, cancellationToken);
        if (result.IsProblem)
        {
            return result.Problem.ToActionResult();
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Gets the party list for the list of party ids.
    /// </summary>
    /// <param name="sender">The request sender.</param>
    /// <param name="partyIds">List of partyIds for parties to retrieve.</param>
    /// <param name="fetchSubUnits">flag indicating whether subunits should be included</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>List of parties based on the partyIds.</returns>
    [HttpPost("partylist")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<ActionResult<List<V1Models.Party>>> GetPartyListForPartyIds(
        [FromServices] IRequestSender<GetV1PartyListByIdRequest, IAsyncEnumerable<V1Models.Party>> sender,
        [FromBody] List<int> partyIds,
        [FromQuery] bool fetchSubUnits = false,
        CancellationToken cancellationToken = default)
    {
        ValidationProblemBuilder builder = default;

        builder.TryValidate(
            path: "/",
            partyIds.AsEnumerable(),
            ListValidator.ForEnumerable<int, uint, PartyIdValidator>(default),
            out IReadOnlyList<uint>? validPartyIds);

        if (builder.TryBuild(out var validationProblem))
        {
            return validationProblem.ToActionResult();
        }

        Debug.Assert(validPartyIds is not null);
        var result = await sender.Send(new GetV1PartyListByIdRequest(validPartyIds, fetchSubUnits), cancellationToken);
        if (result.IsProblem)
        {
            return result.Problem.ToActionResult();
        }

        var parties = await result.Value.ToListAsync(cancellationToken);
        if (parties.Count < 1)
        {
            return NotFound();
        }

        return Ok(parties);
    }

    /// <summary>
    /// Gets the party list for the list of party uuids.
    /// </summary>
    /// <param name="sender">The request sender.</param>
    /// <param name="partyUuids">List of partyUuids for parties to retrieve.</param>
    /// <param name="fetchSubUnits">flag indicating whether subunits should be included</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>List of parties based on the party uuids.</returns>
    [HttpPost("partylistbyuuid")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<ActionResult<List<V1Models.Party>>> GetPartyListForPartyUuids(
        [FromServices] IRequestSender<GetV1PartyListByUuidRequest, IAsyncEnumerable<V1Models.Party>> sender,
        [FromBody] List<Guid> partyUuids,
        [FromQuery] bool fetchSubUnits = false,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetV1PartyListByUuidRequest(partyUuids, fetchSubUnits), cancellationToken);
        if (result.IsProblem)
        {
            return result.Problem.ToActionResult();
        }

        List<V1Models.Party> parties = await result.Value.ToListAsync(cancellationToken);
        return Ok(parties);
    }

    /// <summary>
    /// Gets a set of party identifiers given a list of party uuids or org.nos.
    /// </summary>
    /// <param name="sender">The request sender.</param>
    /// <param name="idsQuery">The party ids.</param>
    /// <param name="uuidsQuery">The party uuids.</param>
    /// <param name="orgNosQuery">The org.nos.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A set of <see cref="PartyIdentifiers"/> for each of the requested parties.</returns>
    [HttpGet("identifiers")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<ActionResult<IAsyncEnumerable<PartyIdentifiers>>> GetPartyIdentifiers(
        [FromServices] IRequestSender<GetV1PartyIdentifiersRequest, IAsyncEnumerable<PartyIdentifiers>> sender,
        [FromQuery(Name = "ids")] List<string?>? idsQuery = null,
        [FromQuery(Name = "uuids")] List<string?>? uuidsQuery = null,
        [FromQuery(Name = "orgs")] List<string?>? orgNosQuery = null,
        CancellationToken cancellationToken = default)
    {
        ValidationProblemBuilder builder = default;
        int count = 0;
        List<uint>? ids = null;
        List<Guid>? uuids = null;
        List<OrganizationIdentifier>? orgNos = null;

        if (idsQuery is { Count: > 0 })
        {
            ids = new List<uint>();
            foreach (var idString in idsQuery.Where(x => x is not null).SelectMany(idsQuery => idsQuery!.Split(',')))
            {
                if (!uint.TryParse(idString, NumberStyles.None, provider: null, out uint id))
                {
                    builder.Add(ValidationErrors.InvalidPartyId, "$QUERY/ids", $"Invalid id: {idString}");
                    continue;
                }

                ids.Add(id);
            }

            count += ids.Count;
        }

        if (uuidsQuery is { Count: > 0 })
        {
            uuids = new List<Guid>();
            foreach (var uuidString in uuidsQuery.Where(x => x is not null).SelectMany(uuidsQuery => uuidsQuery!.Split(',')))
            {
                if (!Guid.TryParse(uuidString, out Guid uuid))
                {
                    builder.Add(ValidationErrors.InvalidPartyUuid, "$QUERY/uuids", $"Invalid uuid: {uuidString}");
                    continue;
                }

                uuids.Add(uuid);
            }

            count += uuids.Count;
        }

        if (orgNosQuery is { Count: > 0 })
        {
            orgNos = new List<OrganizationIdentifier>();
            foreach (var orgNo in orgNosQuery.Where(x => x is not null).SelectMany(orgNosQuery => orgNosQuery!.Split(',')))
            {
                if (!OrganizationIdentifier.TryParse(orgNo, provider: null, out var organizationIdentifier))
                {
                    builder.Add(ValidationErrors.InvalidOrganizationNumber, "$QUERY/orgs", $"Invalid organization number: {orgNo}");
                    continue;
                }

                orgNos.Add(organizationIdentifier);
            }

            count += orgNos.Count;
        }

        if (count > 100)
        {
            builder.Add(ValidationErrors.TooManyItems, ["$QUERY/ids", "$QUERY/uuids", "$QUERY/orgs"], "Maximum number of identifiers is 100");
        }

        if (count == 0)
        {
            builder.Add(StdValidationErrors.Required, ["$QUERY/ids", "$QUERY/uuids", "$QUERY/orgs"], "At least one of the query parameters 'ids', 'uuids' or 'orgs' must be provided");
        }

        if (builder.TryBuild(out var validationProblem))
        {
            return validationProblem.ToActionResult();
        }

        var request = new GetV1PartyIdentifiersRequest(ids, uuids, orgNos);
        var result = await sender.Send(request, cancellationToken);
        if (result.IsProblem)
        {
            return result.Problem.ToActionResult();
        }

        return Ok(result.Value);
    }

    [NonAction]
    private async Task<ActionResult?> AuthorizePartyLookup(V1Models.Party party, CancellationToken cancellationToken)
    {
        var partyId = party.PartyId;
        int? userId = GetUserId(HttpContext);
        if (!userId.HasValue)
        {
            Log.NoUserIdUnauthorized(_logger);
            return Unauthorized();
        }

        if (!PartyIsCallingUser(partyId))
        {
            var validationResult = await _authorization.ValidateSelectedParty(userId.Value, partyId, cancellationToken);
            if (!validationResult.IsSuccess)
            {
                return validationResult.Problem.ToActionResult();
            }

            if (!validationResult.Value)
            {
                Log.UserNotValidatedForParty(_logger, userId: userId.Value, partyId: partyId);
                return Unauthorized();
            }
        }

        return null;
    }

    /// <summary>
    /// Check whether the party id is the user's party id
    /// </summary>
    [NonAction]
    private bool PartyIsCallingUser(int partyId)
    {
        Claim? claim = HttpContext.User.Claims.FirstOrDefault(claim => claim.Type.Equals(AltinnCoreClaimTypes.PartyID));
        return claim != null && (int.Parse(claim.Value) == partyId);
    }

    /// <summary>
    /// Validate if the authenticated identity is an org
    /// </summary>
    [NonAction]
    private static bool IsOrg(HttpContext context)
    {
        return context.User.Claims.Any(claim => claim.Type.Equals(AltinnCoreClaimTypes.Org));
    }

    /// <summary>
    /// Gets userId from httpContext
    /// </summary>
    [NonAction]
    private static int? GetUserId(HttpContext context)
    {
        Claim? claim = context.User.Claims.FirstOrDefault(claim => claim.Type.Equals(AltinnCoreClaimTypes.UserId));

        return claim != null ? Convert.ToInt32(claim.Value) : null;
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Debug, "No userId found in the claims. Returning Unauthorized.")]
        public static partial void NoUserIdUnauthorized(ILogger logger);

        [LoggerMessage(1, LogLevel.Debug, "User {UserId} is not authorized for party {PartyId}.")]
        public static partial void UserNotValidatedForParty(ILogger logger, int userId, int partyId);

        [LoggerMessage(2, LogLevel.Debug, "Party with uuid {PartyUuid} not found. Returning Unauthorized.")]
        public static partial void PartyNotFoundUnauthorized(ILogger logger, Guid partyUuid);

        [LoggerMessage(3, LogLevel.Debug, "Party with id {PartyId} not found. Returning Unauthorized.")]
        public static partial void PartyNotFoundUnauthorized(ILogger logger, int partyId);
    }
}
