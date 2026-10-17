#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Authorization.Localtest.Register;
using Altinn.Platform.Authorization.Services.Interface;
using Authorization.Platform.Authorization.Models;

namespace Altinn.Platform.Authorization.Localtest.Pip;

/// <summary>
/// Backs the legacy <c>authorization/api/v1/roles</c> endpoint (SBL Bridge in production) with the role codes from the
/// register seed. Not on the decision path.
/// </summary>
public sealed class LocaltestRoles(RegisterLocaltestClient register) : IRoles
{
    public async Task<List<Role>> GetDecisionPointRolesForUser(int coveredByUserId, int offeredByPartyId)
    {
        var snapshot = await register.Get();
        var partyUuid = snapshot.Users.Select(u => (u.PartyId, u.PartyUuid))
            .Concat(snapshot.Orgs.Select(o => (o.PartyId, o.PartyUuid)))
            .FirstOrDefault(p => p.PartyId == offeredByPartyId).PartyUuid;
        if (snapshot.UserById(coveredByUserId) is not { } user || partyUuid == default)
        {
            return [];
        }

        return LocaltestRoleMapping.For(snapshot, user, partyUuid).RoleCodes
            .Select(code => new Role { Type = "Altinn", Value = code })
            .ToList();
    }
}
