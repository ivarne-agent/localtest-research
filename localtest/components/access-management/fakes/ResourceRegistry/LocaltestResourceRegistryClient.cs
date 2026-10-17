#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.AccessManagement.Core.Clients.Interfaces;
using Altinn.AccessManagement.Core.Enums.ResourceRegistry;
using Altinn.AccessManagement.Core.Models;
using Altinn.AccessManagement.Core.Models.Consent;
using Altinn.AccessManagement.Core.Models.ResourceRegistry;
using Altinn.Authorization.Api.Contracts.AccessManagement;

namespace Altinn.AccessManagement.Localtest.ResourceRegistry;

/// <summary>
/// Stands in for the Resource Registry client the delegation services use. The production Resource Registry builds
/// the app resources (<c>app_{org}_{app}</c>) from Storage's application list
/// (<c>ResourceRegistryService.MapApplicationToApplicationResource</c> in Altinn/altinn-resource-registry); this
/// serves the same from <see cref="LocaltestAppResources"/>, so every app the runner pushed is a delegable
/// <c>AltinnApp</c> resource. There are no other resources, no subject-resource index and no consent templates.
/// </summary>
public sealed class LocaltestResourceRegistryClient(LocaltestAppResources apps) : IResourceRegistryClient
{
    public async Task<ServiceResource> GetResource(string resourceId, CancellationToken cancellationToken = default)
        => (await GetResourceList(cancellationToken)).Find(r => string.Equals(r.Identifier, resourceId, StringComparison.OrdinalIgnoreCase))!;

    public Task<List<ServiceResource>> GetResources(CancellationToken cancellationToken = default, string? searchParams = null)
        => GetResourceList(cancellationToken);

    public async Task<List<ServiceResource>> GetResourceList(CancellationToken cancellationToken = default)
        => (await apps.Get(cancellationToken)).Select(Map).ToList();

    public Task<IDictionary<string, IEnumerable<BaseAttribute>>> GetSubjectResources(IEnumerable<string> subjects, CancellationToken cancellationToken = default)
        => Task.FromResult<IDictionary<string, IEnumerable<BaseAttribute>>>(new Dictionary<string, IEnumerable<BaseAttribute>>());

    public Task<ConsentTemplate> GetConsentTemplate(string templateId, int? version, CancellationToken cancellationToken = default)
        => Task.FromResult<ConsentTemplate>(null!);

    public Task<List<RightDto>> GetPolicyRightsV2(string resource, string languageCode = "nb", CancellationToken cancellationToken = default)
        => Task.FromResult(new List<RightDto>());

    // Altinn/altinn-resource-registry ResourceRegistryService.MapApplicationToApplicationResource.
    private static ServiceResource Map(LocaltestAppResources.AppResource a) => new()
    {
        Identifier = a.Identifier,
        Title = a.Title,
        ResourceType = a.Identifier.Contains("_a2-") || a.Identifier.Contains("_a1-") ? ResourceType.MigratedApp : ResourceType.AltinnApp,
        ResourceReferences = [new ResourceReference { ReferenceSource = ReferenceSource.Altinn3, ReferenceType = ReferenceType.ApplicationId, Reference = a.AppId }],
        AuthorizationReference =
        [
            new AttributeMatch { Id = "urn:altinn:org", Value = a.Org },
            new AttributeMatch { Id = "urn:altinn:app", Value = a.App },
        ],
        HasCompetentAuthority = new CompetentAuthority { Orgcode = a.Org, Organization = LocaltestAppResources.ServiceOwners.TryGetValue(a.Org, out var o) ? o.OrgNumber : null! },
        Delegable = !a.App.StartsWith("a1-", StringComparison.Ordinal),
        Visible = !a.App.StartsWith("a1-", StringComparison.Ordinal),
    };
}
