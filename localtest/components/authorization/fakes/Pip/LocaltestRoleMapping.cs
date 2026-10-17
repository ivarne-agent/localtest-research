#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Altinn.Platform.Authorization.Localtest.Register;

namespace Altinn.Platform.Authorization.Localtest.Pip;

/// <summary>
/// Resolves what a test user holds for a party from the register seed: the roles the register lists for that party,
/// or, for a sub-unit, the roles held for its main unit (main-unit role holders reach the sub-units, as in
/// Access Management), and the person's own party (privatperson).
/// </summary>
public static class LocaltestRoleMapping
{
    /// <summary>Altinn role code a person has for their own party.</summary>
    public const string SelfRoleCode = "PRIV";

    public static Grants For(RegisterLocaltestClient.Snapshot snapshot, RegisterLocaltestClient.TestUser user, Guid partyUuid)
    {
        if (user.PartyUuid == partyUuid)
        {
            return new Grants([SelfRoleCode], ["privatperson"], [], []);
        }

        var direct = user.Parties.FirstOrDefault(p => p.PartyUuid == partyUuid);
        if (direct is not null)
        {
            return Grants.Of(direct);
        }

        var org = snapshot.Orgs.FirstOrDefault(o => o.PartyUuid == partyUuid);
        if (org?.ParentPartyId is int parent && user.Parties.FirstOrDefault(p => p.PartyId == parent) is { } mainUnit)
        {
            return Grants.Of(mainUnit);
        }

        return Grants.None;
    }

    /// <summary>The parties a user can act for: self, every party with a role, and the sub-units of those.</summary>
    public static IEnumerable<int> PartyIds(RegisterLocaltestClient.Snapshot snapshot, RegisterLocaltestClient.TestUser user)
        => new[] { user.PartyId }
            .Concat(user.Parties.Select(p => p.PartyId))
            .Concat(user.Parties.SelectMany(p => snapshot.SubUnitsOf(p.PartyId)).Select(o => o.PartyId))
            .Distinct();

    public sealed record Grants(IReadOnlyList<string> RoleCodes, IReadOnlyList<string> AltinnRoles, IReadOnlyList<string> CcrRoles, IReadOnlyList<string> AccessPackages)
    {
        public static readonly Grants None = new([], [], [], []);

        public static Grants Of(RegisterLocaltestClient.TestUserParty party)
            => new(party.RoleCodes ?? [], [], party.Roles, party.AccessPackages);

        public IEnumerable<string> RoleUrns()
            => RoleCodes.Select(c => $"urn:altinn:rolecode:{c}")
                .Concat(AltinnRoles.Select(r => $"urn:altinn:role:{r}"))
                .Concat(CcrRoles.Select(r => $"urn:altinn:external-role:ccr:{r}"));

        public IEnumerable<string> AccessPackageUrns()
            => AccessPackages.Select(p => p.StartsWith("urn:", StringComparison.Ordinal) ? p : $"urn:altinn:accesspackage:{p}");
    }
}
