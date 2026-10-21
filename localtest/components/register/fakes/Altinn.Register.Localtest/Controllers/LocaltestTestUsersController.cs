using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Localtest.Persistence;
using Altinn.Register.Localtest.TestData;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Register.Localtest.Controllers;

/// <summary>
/// Localtest-only view of the current test data for the login picker and the other fakes. Anonymous, so the
/// authentication fake needs no platform token to list who can log in.
/// </summary>
[ApiController]
[ApiVersionNeutral]
[AllowAnonymous]
[Route("register/api/localtest")]
public sealed class LocaltestTestUsersController(RegisterStoreProvider provider)
    : ControllerBase
{
    [HttpGet("test-users")]
    public ActionResult<IEnumerable<TestUserDto>> GetTestUsers()
    {
        var store = provider.Current;
        return Ok(store.Users.Select(u => ToDto(store, u)).ToList());
    }

    [HttpGet("organizations")]
    public ActionResult<IEnumerable<TestOrgDto>> GetOrganizations()
    {
        var store = provider.Current;
        return Ok(store.TestData.Orgs
            .Where(o => store.ByPartyId.ContainsKey(checked((uint)o.PartyId)))
            .Select(o =>
            {
                var party = store.ByPartyId[checked((uint)o.PartyId)];
                return new TestOrgDto(
                    party.PartyId.Value,
                    party.PartyUuid.Value,
                    o.OrgNumber,
                    party.DisplayName.Value!,
                    o.ParentPartyId,
                    UnitType(party),
                    [.. o.NotificationAddresses.Select(a => new TestNotificationAddressDto(a.Email, a.MobileNumber))]);
            })
            .ToList());
    }

    private static TestUserDto ToDto(RegisterStore store, TestUser user)
    {
        var p = user.Person;
        var party = user.Party;

        // The parties the user holds something for directly: the own party first (role codes such as PRIV, REGNA or
        // DAGL on oneself, which policies grant on), then other parties. Derived sub-units are not listed here.
        var parties = new List<TestUserPartyDto>
        {
            new(party.PartyId.Value, party.PartyUuid.Value, null, party.DisplayName.Value!, null, [], user.SelfRoleCodes, []),
        };
        parties.AddRange(user.Grants.Select(g => new TestUserPartyDto(
            g.Party.PartyId.Value,
            g.Party.PartyUuid.Value,
            OrgNumber(g.Party),
            g.Party.DisplayName.Value!,
            UnitType(g.Party),
            g.CcrRoles,
            g.RoleCodes,
            g.AccessPackages)));

        // Everything the user may act for, as the old authorization/partylist defined it: self, the parties above,
        // and the sub-units of those organisations.
        var actingFor = new List<ActingForPartyDto>
        {
            Acting(store, party, null, [], user.SelfRoleCodes, []),
        };
        foreach (var grant in user.Grants)
        {
            actingFor.Add(Acting(store, grant.Party, null, grant.CcrRoles, grant.RoleCodes, grant.AccessPackages));
        }

        foreach (var grant in user.Grants)
        {
            foreach (var subUnit in store.SubUnitsOf(grant.Party.PartyUuid.Value))
            {
                if (actingFor.All(a => a.PartyId != subUnit.PartyId.Value))
                {
                    actingFor.Add(Acting(store, subUnit, grant.Party.PartyId.Value, [], [], []));
                }
            }
        }

        // Sub-units the user holds roles for directly still point at their main unit.
        actingFor = [.. actingFor.Select(a => a.ParentPartyId is null && a.PartyType == "organization"
            ? a with { ParentPartyId = store.MainUnitsOf(a.PartyUuid).Select(m => (uint?)m.PartyId.Value).FirstOrDefault() }
            : a)];

        return new TestUserDto(
            p.UserId,
            party.PartyId.Value,
            party.PartyUuid.Value,
            p.Ssn,
            party.DisplayName.Value!,
            p.UserName,
            p.Email,
            p.Language,
            parties,
            p.CustomClaims.Select(c => new TestClaimDto(c.Type, c.Value, c.ValueType)).ToList(),
            party.PartyUuid.Value,
            PartyType(party),
            new TestUserProfileDto(
                p.Language ?? "nb",
                p.PreSelectedPartyId ?? 0,
                p.DoNotPromptForParty ?? false,
                (int)UserType(p),
                UserType(p).ToString()),
            user.SelfRoleCodes,
            actingFor,
            p.Reserved ?? false);
    }

    internal static ProfileUserType UserType(TestPerson person)
        => string.IsNullOrEmpty(person.Ssn) ? ProfileUserType.SelfIdentified : ProfileUserType.SSNIdentified;

    private static ActingForPartyDto Acting(RegisterStore store, PartyRecord party, uint? parentPartyId, IReadOnlyList<string> roles, IReadOnlyList<string> roleCodes, IReadOnlyList<string> accessPackages)
        => new(
            party.PartyId.Value,
            party.PartyUuid.Value,
            PartyType(party),
            OrgNumber(party),
            party.PersonIdentifier.HasValue ? party.PersonIdentifier.Value.ToString() : null,
            party.DisplayName.Value!,
            UnitType(party),
            parentPartyId,
            roles,
            roleCodes,
            accessPackages);

    private static string? UnitType(PartyRecord party)
        => party is OrganizationRecord { UnitType.HasValue: true } org ? org.UnitType.Value : null;

    private static string? OrgNumber(PartyRecord party)
        => party.OrganizationIdentifier.HasValue ? party.OrganizationIdentifier.Value.ToString() : null;

    private static string PartyType(PartyRecord party)
        => party switch
        {
            OrganizationRecord => "organization",
            SelfIdentifiedUserRecord => "self-identified",
            SystemUserRecord => "system-user",
            _ => "person",
        };

    /// <param name="Parties">Parties the user holds role codes, roles or access packages for directly: the own party first (role codes on oneself), then other parties. No derived sub-units.</param>
    /// <param name="UserUuid">The user's UUID (the person's party UUID).</param>
    /// <param name="PartyType"><c>person</c> or <c>self-identified</c>.</param>
    /// <param name="Profile">Profile preferences, as the profile endpoints return them.</param>
    /// <param name="SelfRoleCodes">Altinn role codes for the user's own party, verbatim (<c>PRIV</c>, <c>regna</c>, <c>SELN</c>, ...).</param>
    /// <param name="ActingFor">Every party the user may act for: self first, then <paramref name="Parties"/>, then the sub-units of those.</param>
    /// <param name="Reserved">Reserved in KRR against digital contact (the test data's <c>reserved</c>, default false).</param>
    public sealed record TestUserDto(
        int UserId,
        uint PartyId,
        Guid PartyUuid,
        string Ssn,
        string DisplayName,
        string? UserName,
        string? Email,
        string? Language,
        IReadOnlyList<TestUserPartyDto> Parties,
        IReadOnlyList<TestClaimDto> CustomClaims,
        Guid UserUuid,
        string PartyType,
        TestUserProfileDto Profile,
        IReadOnlyList<string> SelfRoleCodes,
        IReadOnlyList<ActingForPartyDto> ActingFor,
        bool Reserved);

    /// <param name="UserType">The <c>Altinn.Platform.Profile.Enums.UserType</c> number; <paramref name="UserTypeName"/> is its name.</param>
    public sealed record TestUserProfileDto(string Language, int PreSelectedPartyId, bool DoNotPromptForParty, int UserType, string UserTypeName);

    /// <param name="Roles">CCR role identifiers, such as <c>daglig-leder</c>.</param>
    /// <param name="RoleCodes">Altinn role codes as given in the test data, such as <c>DAGL</c> or <c>REGNA</c> (what <c>urn:altinn:rolecode</c> carries). CCR identifiers given in the data appear as their code.</param>
    public sealed record TestUserPartyDto(uint PartyId, Guid PartyUuid, string? OrgNumber, string DisplayName, string? UnitType, IReadOnlyList<string> Roles, IReadOnlyList<string> RoleCodes, IReadOnlyList<string> AccessPackages);

    /// <param name="PartyType"><c>person</c>, <c>organization</c> or <c>self-identified</c>.</param>
    /// <param name="ParentPartyId">For sub-units: the main unit's party id.</param>
    /// <param name="Roles">CCR role identifiers held directly for this party (empty for derived sub-units).</param>
    /// <param name="RoleCodes">Altinn role codes held directly for this party, verbatim.</param>
    public sealed record ActingForPartyDto(uint PartyId, Guid PartyUuid, string PartyType, string? OrgNumber, string? Ssn, string DisplayName, string? UnitType, uint? ParentPartyId, IReadOnlyList<string> Roles, IReadOnlyList<string> RoleCodes, IReadOnlyList<string> AccessPackages);

    /// <param name="NotificationAddresses">Official notification addresses from the test data (what the Profile component seeds its brreg mirror with).</param>
    public sealed record TestOrgDto(uint PartyId, Guid PartyUuid, string OrgNumber, string DisplayName, int? ParentPartyId, string? UnitType, IReadOnlyList<TestNotificationAddressDto> NotificationAddresses);

    public sealed record TestNotificationAddressDto(string? Email, string? MobileNumber);

    public sealed record TestClaimDto(string Type, string Value, string? ValueType);
}
