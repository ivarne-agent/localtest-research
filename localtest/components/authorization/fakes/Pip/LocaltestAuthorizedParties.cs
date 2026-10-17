#nullable enable
using System.Collections.Generic;
using System.Linq;
using Altinn.Authorization.Api.Contracts.AccessManagement;
using Altinn.Authorization.Api.Contracts.AccessManagement.Enums;
using Altinn.Platform.Authorization.Localtest.Register;

namespace Altinn.Platform.Authorization.Localtest.Pip;

/// <summary>
/// The Access Management authorized-parties list for a test user, built from the register seed. One source for the
/// emulated <c>accessmanagement/api/v1/enduser/authorizedparties</c> endpoint and for the PDP's own
/// <c>authorization/api/v1/parties</c> (through <see cref="LocaltestAccessManagementWrapper"/>):
/// <list type="bullet">
/// <item>the person's own party (<c>Person</c>, role <c>priv</c> plus any extra self roles the register lists);</item>
/// <item>every party the register lists roles or access packages for, with lower-case role codes;</item>
/// <item>under each organisation, all its sub-units with the same roles (plus any held directly for the sub-unit);</item>
/// <item>a main unit the user holds nothing for but one of whose sub-units they do, as a hierarchy element
/// (<c>onlyHierarchyElementWithNoAccess</c>), as Access Management does.</item>
/// </list>
/// </summary>
public static class LocaltestAuthorizedParties
{
    public static List<AuthorizedPartyDto> For(RegisterLocaltestClient.Snapshot snapshot, RegisterLocaltestClient.TestUser user, bool includeRoles = true, bool includeAccessPackages = true)
    {
        var self = LocaltestRoleMapping.For(snapshot, user, user.PartyUuid);
        var own = new AuthorizedPartyDto
        {
            PartyUuid = user.PartyUuid,
            PartyId = user.PartyId,
            Name = user.DisplayName,
            PersonId = user.Ssn,
            Type = AuthorizedPartyTypeDto.Person,
        };
        var result = new List<AuthorizedPartyDto> { Fill(own, self, includeRoles, includeAccessPackages) };

        var granted = user.PartyGrants.Where(p => p.PartyId != user.PartyId).Select(p => p.PartyId).ToHashSet();
        var topLevel = new List<int>();
        foreach (var partyId in user.PartyGrants.Select(p => p.PartyId).Where(granted.Contains))
        {
            // A sub-unit is listed under its main unit; one whose main unit the user holds nothing for brings the main
            // unit along as a hierarchy element.
            var top = snapshot.Orgs.FirstOrDefault(o => o.PartyId == partyId)?.ParentPartyId is int parent && snapshot.Orgs.Any(o => o.PartyId == parent)
                ? parent
                : partyId;
            if (!topLevel.Contains(top))
            {
                topLevel.Add(top);
            }
        }

        foreach (var partyId in topLevel)
        {
            var party = Party(snapshot, user, partyId);
            var grants = LocaltestRoleMapping.For(snapshot, user, party.PartyUuid);
            party.OnlyHierarchyElementWithNoAccess = !granted.Contains(partyId);
            Fill(party, grants, includeRoles, includeAccessPackages);
            foreach (var subunit in snapshot.SubUnitsOf(partyId))
            {
                var sub = Party(snapshot, user, subunit.PartyId);
                sub.ParentId = party.PartyUuid;
                var subGrants = LocaltestRoleMapping.For(snapshot, user, sub.PartyUuid);
                if (party.OnlyHierarchyElementWithNoAccess && subGrants.IsEmpty)
                {
                    continue;
                }

                party.Subunits.Add(Fill(sub, subGrants, includeRoles, includeAccessPackages));
            }

            result.Add(party);
        }

        return result;
    }

    private static AuthorizedPartyDto Party(RegisterLocaltestClient.Snapshot snapshot, RegisterLocaltestClient.TestUser user, int partyId)
    {
        var grant = user.PartyGrants.FirstOrDefault(p => p.PartyId == partyId);
        if (snapshot.Orgs.FirstOrDefault(o => o.PartyId == partyId) is { } org)
        {
            return new AuthorizedPartyDto
            {
                PartyUuid = org.PartyUuid,
                PartyId = org.PartyId,
                Name = org.DisplayName ?? grant?.DisplayName,
                OrganizationNumber = org.OrgNumber,
                Type = AuthorizedPartyTypeDto.Organization,
                UnitType = org.UnitType ?? grant?.UnitType ?? (org.ParentPartyId is null ? null : "BEDR"),
                IsDeleted = org.IsDeleted || grant?.IsDeleted == true,
            };
        }

        var person = snapshot.Users.FirstOrDefault(u => u.PartyId == partyId);
        return new AuthorizedPartyDto
        {
            PartyUuid = grant?.PartyUuid ?? person?.PartyUuid ?? default,
            PartyId = partyId,
            Name = grant?.DisplayName ?? person?.DisplayName,
            OrganizationNumber = grant?.OrgNumber,
            PersonId = person?.Ssn,
            Type = grant?.OrgNumber is null ? AuthorizedPartyTypeDto.Person : AuthorizedPartyTypeDto.Organization,
            UnitType = grant?.UnitType,
            IsDeleted = grant?.IsDeleted == true,
        };
    }

    private static AuthorizedPartyDto Fill(AuthorizedPartyDto party, LocaltestRoleMapping.Grants grants, bool includeRoles, bool includeAccessPackages)
    {
        if (includeRoles)
        {
            party.AuthorizedRoles = [.. grants.LowerCaseRoleCodes()];
        }

        if (includeAccessPackages)
        {
            party.AuthorizedAccessPackages = [.. grants.AccessPackageIds()];
        }

        return party;
    }
}
