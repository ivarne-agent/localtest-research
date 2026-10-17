#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Altinn.AccessMgmt.Core.HostedServices;
using Altinn.AccessMgmt.Core.Services.Contracts;
using Altinn.AccessMgmt.PersistenceEF.Audit;
using Altinn.AccessMgmt.PersistenceEF.Extensions;
using Altinn.AccessMgmt.PersistenceEF.Constants;
using Altinn.AccessMgmt.PersistenceEF.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.AccessManagement.Localtest.Sync;

/// <summary>
/// The push side of the app runner contract for Access Management: <c>POST accessmanagement/api/localtest/refresh</c>
/// (and the same at start). It reruns production's Register import from version 0 (the vendored Register's streams
/// are a snapshot, so a full re-poll is the correct refresh), then writes what the streams cannot carry from the
/// same seed: roles on the user's own party, Altinn 2 role codes and access packages, read from the Register's
/// <c>test-users</c> endpoint. Finally it prunes role and package assignments to test users that the seed no longer
/// grants. Everything is written with Access Management's own services under the Register import system audit.
/// </summary>
public sealed class LocaltestRefreshService(
    IServiceProvider services,
    IHttpClientFactory httpClientFactory,
    IOptions<LocaltestAccessManagementOptions> options,
    ILogger<LocaltestRefreshService> logger)
{
    private static readonly string[] StructuralRoleCodes = ["privatperson", "selvregistrert", "hovedenhet", "rettighetshaver"];

    public static async Task RunAtStartup(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LocaltestRefreshService>().RunAsync(importFromRegister: false, CancellationToken.None);
    }

    public async Task<RefreshSummary> RunAsync(bool importFromRegister, CancellationToken cancellationToken)
    {
        if (importFromRegister)
        {
            using var importScope = services.CreateScope();
            await importScope.ServiceProvider.GetRequiredService<RegisterHostedService>().EnsureDbIsIngestWithRegisterData(cancellationToken);
        }

        var url = options.Value.PlatformEndpoint.TrimEnd('/') + "/register/api/localtest/test-users";
        var users = await httpClientFactory.CreateClient().GetFromJsonAsync<List<TestUser>>(url, cancellationToken) ?? [];

        var audit = new AuditValues(SystemEntityConstants.RegisterImportSystem);
        using var scope = services.CreateEFScope(audit);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assignments = scope.ServiceProvider.GetRequiredService<IAssignmentService>();

        var entityIds = (await db.Entities.AsNoTracking().Select(e => e.Id).ToListAsync(cancellationToken)).ToHashSet();
        var expectedRoles = new HashSet<(Guid From, Guid To, Guid Role)>();
        var expectedPackages = new Dictionary<(Guid From, Guid To), HashSet<string>>();
        var summary = new RefreshSummary();

        foreach (var user in users)
        {
            if (!entityIds.Contains(user.PartyUuid))
            {
                logger.LogWarning("Localtest refresh: user {UserId} (party {Party}) is not in the register import, skipped", user.UserId, user.PartyUuid);
                continue;
            }

            foreach (var party in user.Parties ?? [])
            {
                if (!entityIds.Contains(party.PartyUuid))
                {
                    logger.LogWarning("Localtest refresh: party {Party} of user {UserId} is not in the register import, skipped", party.PartyUuid, user.UserId);
                    continue;
                }

                foreach (var code in party.RoleCodes ?? [])
                {
                    if (!RoleConstants.TryGetByCode(code.ToLowerInvariant(), out var role))
                    {
                        summary.UnknownRoleCodes.Add(code);
                        continue;
                    }

                    if (expectedRoles.Add((party.PartyUuid, user.PartyUuid, role.Id)))
                    {
                        await assignments.GetOrCreateAssignmentInternal(party.PartyUuid, user.PartyUuid, role.Entity.Code, cancellationToken);
                        summary.RoleAssignments++;
                    }
                }

                var urns = (party.AccessPackages ?? []).Select(PackageUrn).Distinct().ToList();
                if (urns.Count > 0)
                {
                    var known = await db.Packages.AsNoTracking().Where(p => urns.Contains(p.Urn)).Select(p => p.Urn).ToListAsync(cancellationToken);
                    summary.UnknownPackages.AddRange(urns.Except(known, StringComparer.OrdinalIgnoreCase));
                    var imported = await assignments.ImportAssignmentPackages(party.PartyUuid, user.PartyUuid, known, audit, cancellationToken);
                    summary.PackageAssignments += imported.Count;

                    expectedPackages[(party.PartyUuid, user.PartyUuid)] = urns.ToHashSet(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        // Prune: role assignments to test users (entities with a user id) that neither the seed nor the structural
        // sync roles account for, and imported packages the seed no longer lists.
        var structural = StructuralRoleCodes.Select(c => RoleConstants.TryGetByCode(c, out var r) ? r.Id : Guid.Empty).Where(id => id != Guid.Empty).ToHashSet();
        var testUserIds = users.Select(u => u.PartyUuid).ToHashSet();
        var candidates = await db.Assignments.AsNoTracking()
            .Where(a => testUserIds.Contains(a.ToId) && !structural.Contains(a.RoleId))
            .Select(a => new { a.Id, a.FromId, a.ToId, a.RoleId })
            .ToListAsync(cancellationToken);
        foreach (var stale in candidates.Where(a => !expectedRoles.Contains((a.FromId, a.ToId, a.RoleId))))
        {
            await assignments.DeleteAssignment(stale.Id, cascade: true, audit, cancellationToken);
            summary.PrunedRoleAssignments++;
        }

        var rightholder = RoleConstants.Rightholder.Id;
        var packageRows = await db.AssignmentPackages.AsNoTracking()
            .Where(ap => ap.Assignment!.RoleId == rightholder && testUserIds.Contains(ap.Assignment.ToId))
            .Select(ap => new { ap.Assignment!.FromId, ap.Assignment.ToId, ap.Package!.Urn })
            .ToListAsync(cancellationToken);
        foreach (var group in packageRows.GroupBy(p => (p.FromId, p.ToId)))
        {
            expectedPackages.TryGetValue(group.Key, out var keep);
            var stale = group.Select(p => p.Urn).Where(u => keep is null || !keep.Contains(u)).ToList();
            if (stale.Count > 0)
            {
                summary.PrunedPackageAssignments += await assignments.RevokeImportedAssignmentPackages(group.Key.FromId, group.Key.ToId, stale, audit, onlyRemoveA2Packages: false, cancellationToken);
            }
        }

        logger.LogInformation("Localtest refresh: {Users} users, {Roles} role assignments, {Packages} package assignments, pruned {PrunedRoles} roles and {PrunedPackages} packages; unknown role codes: {UnknownRoles}; unknown packages: {UnknownPackages}",
            users.Count, summary.RoleAssignments, summary.PackageAssignments, summary.PrunedRoleAssignments, summary.PrunedPackageAssignments, string.Join(",", summary.UnknownRoleCodes.Distinct()), string.Join(",", summary.UnknownPackages.Distinct()));
        summary.Users = users.Count;
        return summary;
    }

    private static string PackageUrn(string value)
        => value.StartsWith("urn:", StringComparison.OrdinalIgnoreCase) ? value.ToLowerInvariant() : $"urn:altinn:accesspackage:{value.ToLowerInvariant()}";

    public sealed class RefreshSummary
    {
        public int Users { get; set; }

        public int RoleAssignments { get; set; }

        public int PackageAssignments { get; set; }

        public int PrunedRoleAssignments { get; set; }

        public int PrunedPackageAssignments { get; set; }

        public List<string> UnknownRoleCodes { get; } = [];

        public List<string> UnknownPackages { get; } = [];
    }

    /// <summary>The part of the Register's <c>test-users</c> payload this refresh reads.</summary>
    public sealed class TestUser
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("partyUuid")]
        public Guid PartyUuid { get; set; }

        [JsonPropertyName("parties")]
        public List<TestUserParty>? Parties { get; set; }
    }

    public sealed class TestUserParty
    {
        [JsonPropertyName("partyUuid")]
        public Guid PartyUuid { get; set; }

        [JsonPropertyName("roleCodes")]
        public List<string>? RoleCodes { get; set; }

        [JsonPropertyName("accessPackages")]
        public List<string>? AccessPackages { get; set; }
    }
}
