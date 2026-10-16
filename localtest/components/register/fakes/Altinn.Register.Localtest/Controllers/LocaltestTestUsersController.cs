using Altinn.Register.Localtest.Persistence;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Register.Localtest.Controllers;

/// <summary>
/// Localtest-only view of the seeded test data for the login picker and the other fakes. Anonymous, so the
/// authentication fake needs no platform token to list who can log in.
/// </summary>
[ApiController]
[ApiVersionNeutral]
[AllowAnonymous]
[Route("register/api/localtest")]
public sealed class LocaltestTestUsersController(RegisterStore store)
    : ControllerBase
{
    [HttpGet("test-users")]
    public ActionResult<IEnumerable<TestUserDto>> GetTestUsers()
    {
        var users = store.TestData.Persons
            .Where(p => store.ByPartyId.ContainsKey(checked((uint)p.PartyId)))
            .Select(p =>
            {
                var party = store.ByPartyId[checked((uint)p.PartyId)];
                var parties = store.Assignments
                    .Where(a => a.ToParty == party.PartyUuid.Value && !store.IsMainUnitRole(a))
                    .GroupBy(a => a.FromParty)
                    .Select(g => new TestUserPartyDto(
                        store.ByUuid[g.Key].PartyId.Value,
                        g.Key,
                        store.ByUuid[g.Key].OrganizationIdentifier.HasValue ? store.ByUuid[g.Key].OrganizationIdentifier.Value.ToString() : null,
                        store.ByUuid[g.Key].DisplayName.Value,
                        g.Select(a => a.Identifier).Distinct().ToList(),
                        p.AccessPackages.TryGetValue(store.ByUuid[g.Key].PartyId.Value.ToString(), out var packages) ? packages : []))
                    .ToList();

                return new TestUserDto(
                    p.UserId,
                    party.PartyId.Value,
                    party.PartyUuid.Value,
                    p.Ssn,
                    party.DisplayName.Value,
                    p.UserName,
                    p.Email,
                    p.Language,
                    parties,
                    p.CustomClaims.Select(c => new TestClaimDto(c.Type, c.Value, c.ValueType)).ToList());
            });

        return Ok(users);
    }

    [HttpGet("organizations")]
    public ActionResult<IEnumerable<TestOrgDto>> GetOrganizations()
        => Ok(store.TestData.Orgs
            .Where(o => store.ByPartyId.ContainsKey(checked((uint)o.PartyId)))
            .Select(o =>
            {
                var party = store.ByPartyId[checked((uint)o.PartyId)];
                return new TestOrgDto(party.PartyId.Value, party.PartyUuid.Value, o.OrgNumber, party.DisplayName.Value, o.ParentPartyId);
            }));

    public sealed record TestUserDto(int UserId, uint PartyId, Guid PartyUuid, string Ssn, string DisplayName, string? UserName, string? Email, string? Language, IReadOnlyList<TestUserPartyDto> Parties, IReadOnlyList<TestClaimDto> CustomClaims);

    public sealed record TestUserPartyDto(uint PartyId, Guid PartyUuid, string? OrgNumber, string DisplayName, IReadOnlyList<string> Roles, IReadOnlyList<string> AccessPackages);

    public sealed record TestOrgDto(uint PartyId, Guid PartyUuid, string OrgNumber, string DisplayName, int? ParentPartyId);

    public sealed record TestClaimDto(string Type, string Value, string? ValueType);
}
