#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Altinn.Authorization.ABAC.Xacml;
using Altinn.Platform.Authorization.Helpers;
using Altinn.Platform.Authorization.Models;
using Altinn.Platform.Authorization.Services.Implementation;
using Altinn.Platform.Authorization.Services.Interface;
using Microsoft.Extensions.Caching.Memory;

namespace Altinn.Platform.Authorization.Localtest.Policies;

/// <summary>
/// Runs the production <see cref="PolicyRetrievalPoint"/>, but drops its whole-minute cache entry for an app policy
/// before each read. <see cref="LocaltestPolicyRepository"/> reads the policy file on every call, so a policy the app
/// runner pushes applies to the next decision (production: after the cache timeout, at least a minute).
/// </summary>
public sealed class LocaltestPolicyRetrievalPoint(PolicyRetrievalPoint inner, IMemoryCache memoryCache) : IPolicyRetrievalPoint
{
    public Task<XacmlPolicy> GetPolicyAsync(XacmlContextRequest request)
    {
        if (PolicyHelper.GetPolicyResourceType(request, out _, out var org, out var app) == PolicyResourceType.AltinnApps
            && !string.IsNullOrWhiteSpace(org) && !string.IsNullOrWhiteSpace(app))
        {
            memoryCache.Remove(PolicyHelper.GetAltinnAppsPolicyPath(org, app));
        }

        return inner.GetPolicyAsync(request);
    }

    public Task<XacmlPolicy> GetPolicyAsync(string org, string app)
    {
        memoryCache.Remove(PolicyHelper.GetAltinnAppsPolicyPath(org, app));
        return inner.GetPolicyAsync(org, app);
    }

    public Task<XacmlPolicy> GetPolicyVersionAsync(string policyPath, string version, CancellationToken cancellationToken = default)
        => inner.GetPolicyVersionAsync(policyPath, version, cancellationToken);
}
