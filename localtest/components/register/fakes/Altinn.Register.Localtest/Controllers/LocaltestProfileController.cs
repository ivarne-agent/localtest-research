using System.Text.Json;
using Altinn.Register.Core.Mediator;
using Altinn.Register.Core.Operations;
using Altinn.Register.Localtest.Persistence;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using V1Models = Altinn.Register.Contracts.V1;

namespace Altinn.Register.Localtest.Controllers;

/// <summary>
/// The Profile user endpoints apps and Authentication call (<c>profile/api/v1/</c>), answered from the register's
/// test data. The <c>party</c> is the register's own v1 party, so names and person details match
/// <c>register/api/v1/parties</c>. Authorization as in Profile's <c>UsersController</c>: a user or org token on every
/// <c>users</c> route, plus a platform access token on the lookups apps make; <c>users/current</c> has no
/// <c>altinn:portal/enduser</c> scope check (feature-toggled in Profile). <c>internal/user</c> is anonymous, as in
/// Profile's <c>UserProfileInternalController</c>.
/// </summary>
[ApiController]
[Authorize]
[ApiVersionNeutral]
[Route("profile/api/v1")]
public sealed class LocaltestProfileController(RegisterStoreProvider provider, IRequestSender<GetV1PartyByIdRequest, V1Models.Party> partySender)
    : ControllerBase
{
    private const string UserIdClaim = "urn:altinn:userid";

    [Authorize(Policy = "PlatformAccess")]
    [HttpGet("users/{userId:int}")]
    public Task<ActionResult<UserProfileDto>> GetByUserId(int userId, CancellationToken cancellationToken)
        => Respond(provider.Current.UsersByUserId.GetValueOrDefault(userId), cancellationToken);

    [Authorize(Policy = "PlatformAccess")]
    [HttpGet("users/byuuid/{userUuid:guid}")]
    public Task<ActionResult<UserProfileDto>> GetByUuid(Guid userUuid, CancellationToken cancellationToken)
        => Respond(provider.Current.UserByPartyUuid(userUuid), cancellationToken);

    /// <summary>
    /// Lookup by body: a JSON string (the national identity number, what app-lib sends) or an object with
    /// <c>ssn</c>, <c>userId</c>, <c>userUuid</c> or <c>username</c> (the Profile <c>UserProfileLookup</c>).
    /// </summary>
    [Authorize(Policy = "PlatformAccess")]
    [HttpPost("users")]
    public Task<ActionResult<UserProfileDto>> Lookup([FromBody] JsonElement body, CancellationToken cancellationToken)
        => LookupCore(body, cancellationToken);

    /// <summary>What production Authentication calls; same lookup as <c>POST users</c>.</summary>
    [AllowAnonymous]
    [HttpPost("internal/user")]
    public Task<ActionResult<UserProfileDto>> InternalLookup([FromBody] JsonElement body, CancellationToken cancellationToken)
        => LookupCore(body, cancellationToken);

    [HttpGet("users/current")]
    public async Task<ActionResult<UserProfileDto>> GetCurrent(CancellationToken cancellationToken)
    {
        var claim = User.FindFirst(UserIdClaim)?.Value;
        if (!int.TryParse(claim, out var userId))
        {
            return BadRequest("Invalid request context. UserId must be provided in claims.");
        }

        return await Respond(provider.Current.UsersByUserId.GetValueOrDefault(userId), cancellationToken);
    }

    private Task<ActionResult<UserProfileDto>> LookupCore(JsonElement body, CancellationToken cancellationToken)
    {
        var store = provider.Current;
        TestUser? user = null;

        if (body.ValueKind == JsonValueKind.String)
        {
            user = store.UserBySsn(body.GetString() ?? string.Empty);
        }
        else if (body.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in body.EnumerateObject())
            {
                user ??= property.Name.ToLowerInvariant() switch
                {
                    "ssn" when property.Value.ValueKind == JsonValueKind.String => store.UserBySsn(property.Value.GetString()!),
                    "userid" when property.Value.TryGetInt32(out var id) && id != 0 => store.UsersByUserId.GetValueOrDefault(id),
                    "useruuid" when property.Value.ValueKind == JsonValueKind.String && Guid.TryParse(property.Value.GetString(), out var uuid) => store.UserByPartyUuid(uuid),
                    "username" when property.Value.ValueKind == JsonValueKind.String => store.UsersByUsername.GetValueOrDefault(property.Value.GetString()!),
                    _ => null,
                };
            }
        }
        else
        {
            return Task.FromResult<ActionResult<UserProfileDto>>(BadRequest("Expected a national identity number string or a lookup object (ssn, userId, userUuid or username)."));
        }

        return Respond(user, cancellationToken);
    }

    private async Task<ActionResult<UserProfileDto>> Respond(TestUser? user, CancellationToken cancellationToken)
    {
        if (user is null)
        {
            return NotFound();
        }

        var result = await partySender.Send(new GetV1PartyByIdRequest(user.Party.PartyId.Value), cancellationToken);
        if (result.IsProblem)
        {
            return NotFound(); // the user's party did not make it into the register (see the store warnings)
        }

        var p = user.Person;
        var userType = LocaltestTestUsersController.UserType(p);
        return Ok(new UserProfileDto(
            p.UserId,
            user.Party.PartyUuid.Value,
            p.UserName,
            null,
            false,
            p.TelephoneNumber ?? p.MobileNumber,
            p.Email,
            p.PartyId,
            result.Value,
            (int)userType,
            new ProfileSettingPreferenceDto(p.Language ?? "nb", p.PreSelectedPartyId ?? 0, p.DoNotPromptForParty ?? false)));
    }

    /// <summary>Shape of <c>Altinn.Platform.Profile.Models.UserProfile</c>; <c>userType</c> is the enum number.</summary>
    public sealed record UserProfileDto(
        int UserId,
        Guid UserUuid,
        string? UserName,
        string? ExternalIdentity,
        bool IsReserved,
        string? PhoneNumber,
        string? Email,
        int PartyId,
        V1Models.Party Party,
        int UserType,
        ProfileSettingPreferenceDto ProfileSettingPreference);

    public sealed record ProfileSettingPreferenceDto(string Language, int PreSelectedPartyId, bool DoNotPromptForParty);
}
