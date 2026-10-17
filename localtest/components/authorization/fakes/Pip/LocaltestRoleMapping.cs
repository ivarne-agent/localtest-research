#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Altinn.Platform.Authorization.Localtest.Register;

namespace Altinn.Platform.Authorization.Localtest.Pip;

/// <summary>
/// Resolves what a test user holds for a party from the register seed: the roles the register lists for that party,
/// for a sub-unit also the roles held for its main unit (main-unit role holders reach the sub-units, as in
/// Access Management), and for the person's own party <c>PRIV</c> / <c>privatperson</c> plus whatever the register
/// lists for it.
/// </summary>
/// <remarks>
/// Role codes are emitted in both cases: production Access Management sends lower-case codes in
/// <c>urn:altinn:rolecode</c> (what Studio app policies match: <c>dagl</c>, <c>regna</c>), while older policies and the
/// register seed use upper-case (<c>DAGL</c>). XACML <c>string-equal</c> is case-sensitive, so both forms are needed.
/// </remarks>
public static class LocaltestRoleMapping
{
    /// <summary>Altinn role code a person has for their own party.</summary>
    public const string SelfRoleCode = "PRIV";

    /// <summary>Altinn 3 role a person has for their own party (<c>urn:altinn:role:privatperson</c>).</summary>
    public const string SelfAltinnRole = "privatperson";

    public static Grants For(RegisterLocaltestClient.Snapshot snapshot, RegisterLocaltestClient.TestUser user, Guid partyUuid)
    {
        var direct = Grants.Of(user.PartyGrants.FirstOrDefault(p => p.PartyUuid == partyUuid));
        if (user.PartyUuid == partyUuid)
        {
            return new Grants([SelfRoleCode], [SelfAltinnRole], [], []).With(direct);
        }

        var org = snapshot.Orgs.FirstOrDefault(o => o.PartyUuid == partyUuid);
        var mainUnit = org?.ParentPartyId is int parent ? user.PartyGrants.FirstOrDefault(p => p.PartyId == parent) : null;
        return direct.With(Grants.Of(mainUnit));
    }

    /// <summary>The parties a user can act for: self, every party with a role, and the sub-units of those.</summary>
    public static IEnumerable<int> PartyIds(RegisterLocaltestClient.Snapshot snapshot, RegisterLocaltestClient.TestUser user)
        => new[] { user.PartyId }
            .Concat(user.PartyGrants.Select(p => p.PartyId))
            .Concat(user.PartyGrants.SelectMany(p => snapshot.SubUnitsOf(p.PartyId)).Select(o => o.PartyId))
            .Distinct();

    /// <summary>Lower-case first, then the other casings, without duplicates.</summary>
    public static IEnumerable<string> BothCases(IEnumerable<string> codes)
    {
        var list = codes.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        return list.Select(c => c.ToLowerInvariant())
            .Concat(list.Select(c => c.ToUpperInvariant()))
            .Concat(list)
            .Distinct(StringComparer.Ordinal);
    }

    /// <summary>What a user holds for a party. Role codes as seeded (any case); <see cref="RoleUrns"/> emits both cases.</summary>
    public sealed record Grants(IReadOnlyList<string> RoleCodes, IReadOnlyList<string> AltinnRoles, IReadOnlyList<string> CcrRoles, IReadOnlyList<string> AccessPackages)
    {
        public static readonly Grants None = new([], [], [], []);

        public static Grants Of(RegisterLocaltestClient.TestUserParty? party)
            => party is null ? None : new(party.RoleCodes ?? [], [], party.Roles ?? [], party.AccessPackages ?? []);

        public bool IsEmpty => RoleCodes.Count == 0 && AltinnRoles.Count == 0 && CcrRoles.Count == 0 && AccessPackages.Count == 0;

        public Grants With(Grants other)
            => new(
                RoleCodes.Concat(other.RoleCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                AltinnRoles.Concat(other.AltinnRoles).Distinct().ToList(),
                CcrRoles.Concat(other.CcrRoles).Distinct().ToList(),
                AccessPackages.Concat(other.AccessPackages).Distinct().ToList());

        /// <summary>Role codes in lower case, as production Access Management lists them in <c>authorizedRoles</c>.</summary>
        public IEnumerable<string> LowerCaseRoleCodes() => RoleCodes.Select(c => c.ToLowerInvariant()).Distinct();

        public IEnumerable<string> RoleUrns()
            => BothCases(RoleCodes).Select(c => $"urn:altinn:rolecode:{c}")
                .Concat(AltinnRoles.Select(r => $"urn:altinn:role:{r}"))
                .Concat(CcrRoles.Select(r => $"urn:altinn:external-role:ccr:{r}"));

        public IEnumerable<string> AccessPackageUrns()
            => AccessPackages.Select(p => p.StartsWith("urn:", StringComparison.Ordinal) ? p : $"urn:altinn:accesspackage:{p}");

        /// <summary>Access package identifiers without the urn prefix, as in <c>authorizedAccessPackages</c>.</summary>
        public IEnumerable<string> AccessPackageIds()
            => AccessPackages.Select(p => p.StartsWith("urn:altinn:accesspackage:", StringComparison.Ordinal) ? p["urn:altinn:accesspackage:".Length..] : p);
    }
}
