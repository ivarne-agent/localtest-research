#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Authorization.ABAC.Xacml;
using Altinn.Authorization.Api.Contracts.Register;
using Altinn.Authorization.Api.Contracts.ResourceRegistry;
using Altinn.Authorization.Models.ResourceRegistry;
using Altinn.Platform.Authorization.Helpers;
using Altinn.Platform.Authorization.Services.Interface;
using Altinn.ResourceRegistry.Models;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Authorization.Localtest.Pip;

/// <summary>
/// Resource Registry PIP/PRP over the policies folder: <c>resourceregistry/{id}/policy.xml</c> and an optional
/// <c>resourceregistry/{id}/resource.json</c> (the Resource Registry <c>ServiceResource</c> JSON). No access lists.
/// </summary>
public sealed class LocaltestResourceRegistry(IOptions<LocaltestAuthorizationOptions> options) : IResourceRegistry
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<ServiceResource?> GetResourceAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        var file = PathFor(resourceId, "resource.json");
        if (file is null || !File.Exists(file))
        {
            return null;
        }

        await using var stream = File.OpenRead(file);
        return await JsonSerializer.DeserializeAsync<ServiceResource>(stream, _json, cancellationToken);
    }

    public Task<XacmlPolicy?> GetResourcePolicyAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        var file = PathFor(resourceId, "policy.xml");
        if (file is null || !File.Exists(file))
        {
            return Task.FromResult<XacmlPolicy?>(null);
        }

        using var stream = File.OpenRead(file);
        return Task.FromResult<XacmlPolicy?>(PolicyHelper.ParsePolicy(stream));
    }

    public Task<IEnumerable<AccessListResourceMembershipWithActionFilterDto>> GetMembershipsForResourceForParty(PartyUrn partyUrn, ResourceIdUrn resourceIdUrn, CancellationToken cancellationToken = default)
        => Task.FromResult(Enumerable.Empty<AccessListResourceMembershipWithActionFilterDto>());

    public Task<IEnumerable<AccessListInfoDto>> GetMembershipsForParty(PartyUrn.PartyUuid partyUrn, CancellationToken cancellationToken = default)
        => Task.FromResult(Enumerable.Empty<AccessListInfoDto>());

    private string? PathFor(string resourceId, string fileName)
        => string.IsNullOrWhiteSpace(resourceId) || resourceId.IndexOfAny(['/', '\\', '.']) >= 0
            ? null
            : Path.Combine(options.Value.PoliciesPath, "resourceregistry", resourceId, fileName);
}
