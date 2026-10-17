#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Altinn.Platform.Authorization.Models.Oed;
using Altinn.Platform.Authorization.Services.Interface;

namespace Altinn.Platform.Authorization.Localtest.Pip;

/// <summary>No digitalt dødsbo (OED) role assignments locally; production calls an external API through Maskinporten.</summary>
public sealed class NoOedRoleAssignments : IOedRoleAssignmentWrapper
{
    public Task<List<OedRoleAssignment>> GetOedRoleAssignments(string from, string to) => Task.FromResult(new List<OedRoleAssignment>());
}
