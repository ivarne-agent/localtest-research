using Digdir.Domain.Dialogporten.Application.Common.Behaviours.FeatureMetric;
using Digdir.Domain.Dialogporten.Application.Externals;
using Digdir.Domain.Dialogporten.Domain.SubjectResources;
using Digdir.Domain.Dialogporten.Infrastructure.Altinn.AccessManagement;
using Digdir.Domain.Dialogporten.Infrastructure.Altinn.Authorization;
using Digdir.Domain.Dialogporten.Infrastructure.Persistence.Repositories;
using Digdir.Domain.Dialogporten.Infrastructure.ServiceResourceMetadata;
using Digdir.Domain.Dialogporten.Application.Externals.AltinnAuthorization;
using ZiggyCreatures.Caching.Fusion;

namespace Digdir.Domain.Dialogporten.Infrastructure.Localtest;

/// <summary>
/// Every named FusionCache production registers (<c>InfrastructureExtensions.AddInfrastructure</c>), named by the same
/// constants, so a rename upstream breaks this build instead of silently leaving a cache out. All of them hold data
/// derived from the Resource Registry, Register, Authorization or Access Management, or from the dialogs themselves; in
/// production they expire after 5 minutes to 24 hours and nothing clears them.
/// </summary>
public static class LocaltestCaches
{
    public static IReadOnlyList<string> Names { get; } =
    [
        nameof(Altinn.NameRegistry),
        nameof(Altinn.ResourceRegistry),
        nameof(Altinn.OrganizationRegistry),
        nameof(Altinn.Authorization),
        nameof(AuthorizedPartiesResult),
        nameof(SubjectResource),
        nameof(IFeatureMetricServiceResourceCache),
        nameof(IPartyResourceReferenceRepository),
        PartyResourceRepository.ReferencedResourcesCacheName,
        SubjectResourceRepository.ReferencedPartyResourcesCacheName,
        ResourcePolicyInformationRepository.MinimumAuthenticationLevelsCacheName,
        AccessManagementMetadataClient.CacheName,
        ServiceResourceMetadataCatalogue.CacheName,
        AuthorizedServiceResourcesProvider.CacheName,
    ];

    /// <summary>
    /// Clears every named cache: the memory tier of this host, the entries in Redis (FusionCache records the clear in the
    /// distributed tier, so older entries there count as gone) and, through the Redis backplane, the memory tier of the
    /// GraphQL and Service hosts. <c>allowFailSafe: false</c> removes the entries instead of only expiring them, so a
    /// stale value is never served as a fail-safe fallback afterwards.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ClearAll(IFusionCacheProvider provider, CancellationToken cancellationToken)
    {
        foreach (var name in Names)
            await provider.GetCache(name).ClearAsync(allowFailSafe: false, token: cancellationToken);
        return Names;
    }
}
