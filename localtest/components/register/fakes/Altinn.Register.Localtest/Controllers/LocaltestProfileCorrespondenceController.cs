using Altinn.Register.Localtest.Persistence;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static Altinn.Register.Localtest.Controllers.LocaltestProfileContactPointsController;

namespace Altinn.Register.Localtest.Controllers;

/// <summary>
/// Profile's <c>CorrespondenceController</c> (<c>profile/api/v1/correspondence/</c>): the two contact point lookups Altinn
/// Correspondence calls, with the same answers as the anonymous organisation lookups Notifications uses
/// (<see cref="LocaltestProfileContactPointsController"/>). As in Profile they require a token with the scope
/// <c>altinn:profile/correspondence.notificationsettings.read</c> (policy <see cref="PolicyName"/>), which Correspondence's
/// exchanged Maskinporten token carries: 401 without a token, 403 without the scope.
/// </summary>
[ApiController]
[Authorize(Policy = PolicyName)]
[ApiVersionNeutral]
[Route("profile/api/v1/correspondence")]
public sealed class LocaltestProfileCorrespondenceController(RegisterStoreProvider provider) : ControllerBase
{
    public const string PolicyName = "LocaltestProfileCorrespondenceAccess";
    public const string Scope = "altinn:profile/correspondence.notificationsettings.read";

    /// <summary>Users' contact points registered for organisations: every test user holding something at the organisation.</summary>
    [HttpPost("units/contactpoint/lookup")]
    public ActionResult<ContactPointsList<OrganizationContactPoints>> LookupUnits([FromBody] UnitLookup lookup)
        => Ok(new ContactPointsList<OrganizationContactPoints>([.. OrganizationEntries(provider.Current, lookup.OrganizationNumbers, withUsers: true, lookup.ResourceId)]));

    /// <summary>Official notification addresses (the test data's <c>notificationAddresses</c>, the main unit's as fallback).</summary>
    [HttpPost("organizations/notificationaddresses/lookup")]
    public ActionResult<ContactPointsList<OrganizationNotificationAddresses>> LookupOrganizations([FromBody] OrganizationLookup lookup)
        => Ok(new ContactPointsList<OrganizationNotificationAddresses>([.. OfficialAddresses(provider.Current, lookup.OrganizationNumbers)]));

    /// <summary>Profile's <c>ScopeAccessRequirement</c>: one of the token's space-separated <c>scope</c> values.</summary>
    public static void AddPolicy(AuthorizationBuilder authorization)
        => authorization.AddPolicy(PolicyName, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.FindAll("scope")
                .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Contains(Scope, StringComparer.Ordinal)));
}
