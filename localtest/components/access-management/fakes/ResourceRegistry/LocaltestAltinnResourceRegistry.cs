#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Authorization.Integration.Platform;
using Altinn.Authorization.Integration.Platform.ResourceRegistry;

namespace Altinn.AccessManagement.Localtest.ResourceRegistry;

/// <summary>
/// Stands in for the Resource Registry behind the production <c>ResourceSyncService</c> (which fills
/// <c>dbo.provider</c>, <c>dbo.resource</c>, <c>dbo.roleresource</c> and <c>dbo.packageresource</c>): the service
/// owners of the apps in Storage, and one update per (app resource, policy subject), all from
/// <see cref="LocaltestAppResources"/>. The stream is a snapshot: one page, no next link, and every item is
/// "updated now", so each run of the sync re-applies everything.
/// </summary>
public sealed class LocaltestAltinnResourceRegistry(LocaltestAppResources apps, TimeProvider timeProvider) : IAltinnResourceRegistry
{
    public async Task<IAsyncEnumerable<PlatformResponse<PageStream<ResourceUpdatedModel>>>> StreamResources(DateTime since = default, string? nextPage = null, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var data = (await apps.Get(cancellationToken))
            .SelectMany(a => (a.SubjectUrns.Count > 0 ? a.SubjectUrns : [$"urn:altinn:org:{a.Org}"])
                .Select(subject => new ResourceUpdatedModel { ResourceUrn = "urn:altinn:resource:" + a.Identifier, SubjectUrn = subject, UpdatedAt = now, Deleted = false }))
            .ToList();
        var page = new PageStream<ResourceUpdatedModel>
        {
            Data = string.IsNullOrEmpty(nextPage) ? data : [],
            Links = new PageStream<ResourceUpdatedModel>.LinksStream { Next = null },
            Stats = new PageStream<ResourceUpdatedModel>.StatsStream { PageStart = 1, PageEnd = (ulong)data.Count, SequenceMax = (ulong)data.Count },
        };
        return One(Ok(page));
    }

    public async Task<PlatformResponse<ResourceModel>> GetResource(string id, CancellationToken cancellationToken = default)
    {
        var app = (await apps.Get(cancellationToken)).FirstOrDefault(a => string.Equals(a.Identifier, id, StringComparison.OrdinalIgnoreCase));
        return app is null
            ? new PlatformResponse<ResourceModel> { StatusCode = HttpStatusCode.NotFound, IsSuccessful = false }
            : Ok(ToModel(app));
    }

    public async Task<PlatformResponse<List<ResourceModel>>> GetResources(CancellationToken cancellationToken = default)
        => Ok((await apps.Get(cancellationToken)).Select(ToModel).ToList());

    public async Task<PlatformResponse<ServiceOwners>> GetServiceOwners(CancellationToken cancellationToken = default)
    {
        var orgs = new Dictionary<string, ServiceOwner>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in (await apps.Get(cancellationToken)).Select(a => a.Org).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var known = LocaltestAppResources.ServiceOwners.TryGetValue(code, out var owner) ? owner : (OrgNumber: string.Empty, Name: code.ToUpperInvariant());
            orgs[code] = new ServiceOwner
            {
                Id = LocaltestAppResources.ServiceOwnerId(code),
                Name = new ServiceOwnerName { Nb = known.Name, Nn = known.Name, En = known.Name },
                Orgnr = known.OrgNumber,
                Logo = string.Empty,
                Homepage = string.Empty,
                Environments = ["localtest"],
            };
        }

        return Ok(new ServiceOwners { Orgs = orgs });
    }

    // Altinn/altinn-resource-registry ResourceRegistryService.MapApplicationToApplicationResource.
    private static ResourceModel ToModel(LocaltestAppResources.AppResource a) => new()
    {
        Identifier = a.Identifier,
        Title = new ResourceTitle { Nb = a.Title.GetValueOrDefault("nb") ?? a.App, Nn = a.Title.GetValueOrDefault("nn") ?? a.Title.GetValueOrDefault("nb") ?? a.App, En = a.Title.GetValueOrDefault("en") ?? a.Title.GetValueOrDefault("nb") ?? a.App },
        Description = new ResourceDescription { Nb = "-", Nn = "-", En = "-" },
        Status = "Active",
        ResourceType = a.Identifier.Contains("_a2-") || a.Identifier.Contains("_a1-") ? "MigratedApp" : "AltinnApp",
        Delegable = !a.App.StartsWith("a1-", StringComparison.Ordinal),
        Visible = !a.App.StartsWith("a1-", StringComparison.Ordinal),
        HasCompetentAuthority = new ResourceHasCompetentAuthority { Orgcode = a.Org, Organization = LocaltestAppResources.ServiceOwners.TryGetValue(a.Org, out var o) ? o.OrgNumber : string.Empty },
        ResourceReferences = [new ResourceReference { ReferenceSource = "Altinn3", ReferenceType = "ApplicationId", Reference = a.AppId }],
    };

    private static PlatformResponse<T> Ok<T>(T content) => new() { StatusCode = HttpStatusCode.OK, IsSuccessful = true, Content = content };

    private static async IAsyncEnumerable<T> One<T>(T item)
    {
        await Task.CompletedTask;
        yield return item;
    }
}
