#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authorization.Repositories.Interface;
using Azure;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Authorization.Localtest.Policies;

/// <summary>
/// Replaces the Azure Blob policy store. An app policy (<c>{org}/{app}/policy.xml</c> in the <c>metadata</c>
/// container) comes from, in order: the mounted policies folder, a policy posted to <c>authorization/api/v1/policies</c>
/// since start, or the running app (<c>{org}/{app}/api/v1/meta/authorizationpolicy</c>), as today's localtest does.
/// <c>[ORG]</c>/<c>[APP]</c> placeholders are replaced, also as today's localtest does. Delegation policies
/// (<c>delegationpolicies</c> container) do not exist locally. Reads are reused for a few seconds.
/// </summary>
public sealed class LocaltestPolicyRepository(
    HttpClient httpClient,
    IOptions<LocaltestAuthorizationOptions> options,
    TimeProvider timeProvider,
    ILogger<LocaltestPolicyRepository> logger)
    : IPolicyRepository
{
    private readonly ConcurrentDictionary<string, string> _posted = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, string? Policy)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<Stream> GetPolicyAsync(string filepath, CancellationToken cancellationToken = default)
    {
        if (filepath.Contains("delegationpolicy.xml", StringComparison.Ordinal))
        {
            return new MemoryStream();
        }

        var now = timeProvider.GetUtcNow();
        if (!_cache.TryGetValue(filepath, out var cached) || now - cached.At >= TimeSpan.FromSeconds(options.Value.PolicyCacheSeconds))
        {
            cached = (now, await Load(filepath, cancellationToken));
            _cache[filepath] = cached;
        }

        return cached.Policy is null ? new MemoryStream() : new MemoryStream(Encoding.UTF8.GetBytes(cached.Policy), writable: false);
    }

    public Task<Stream> GetPolicyVersionAsync(string filepath, string version, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream>(new MemoryStream());

    public async Task<Response<BlobContentInfo>> WritePolicyAsync(string filepath, Stream fileStream, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(fileStream);
        _posted[filepath] = await reader.ReadToEndAsync(cancellationToken);
        _cache.TryRemove(filepath, out _);
        logger.LogInformation("Localtest: stored posted policy {Path} in memory", filepath);
        var info = BlobsModelFactory.BlobContentInfo(new ETag(Guid.NewGuid().ToString()), timeProvider.GetUtcNow(), null, Guid.NewGuid().ToString(), null, null, 0);
        return Response.FromValue(info, new LocaltestResponse((int)HttpStatusCode.Created));
    }

    public Task<Response<BlobContentInfo>> WritePolicyConditionallyAsync(string filepath, Stream fileStream, string blobLeaseId, CancellationToken cancellationToken = default)
        => WritePolicyAsync(filepath, fileStream, cancellationToken);

    public Task<Response> DeletePolicyVersionAsync(string filepath, string version, CancellationToken cancellationToken = default)
        => Task.FromResult<Response>(new LocaltestResponse((int)HttpStatusCode.Accepted));

    public Task<string> TryAcquireBlobLease(string filepath, CancellationToken cancellationToken = default)
        => Task.FromResult(Guid.NewGuid().ToString());

    public Task ReleaseBlobLease(string filepath, string leaseId) => Task.CompletedTask;

    public async Task<bool> PolicyExistsAsync(string filepath, CancellationToken cancellationToken = default)
    {
        using var stream = await GetPolicyAsync(filepath, cancellationToken);
        return stream.Length > 0;
    }

    private async Task<string?> Load(string filepath, CancellationToken cancellationToken)
    {
        // Production path: {org}/{app}/policy.xml (PolicyHelper.GetAltinnAppsPolicyPath).
        var parts = filepath.Split('/');
        if (parts.Length != 3)
        {
            return null;
        }

        string org = parts[0], app = parts[1];
        var file = Path.Combine(options.Value.PoliciesPath, org, app, "policy.xml");
        string? policy = null;
        if (File.Exists(file))
        {
            policy = await File.ReadAllTextAsync(file, cancellationToken);
        }
        else if (_posted.TryGetValue(filepath, out var posted))
        {
            policy = posted;
        }
        else if (options.Value.FetchPoliciesFromApps)
        {
            policy = await FetchFromApp(org, app, cancellationToken);
        }

        return policy?.Replace("[org]", org).Replace("[ORG]", org).Replace("[app]", app).Replace("[APP]", app);
    }

    private async Task<string?> FetchFromApp(string org, string app, CancellationToken cancellationToken)
    {
        var url = new Uri(new Uri(options.Value.AppsBaseUrl.TrimEnd('/') + "/"), $"{org}/{app}/api/v1/meta/authorizationpolicy");
        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }

            logger.LogWarning("Localtest: no policy for {Org}/{App}: {Url} answered {Status}", org, app, url, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Localtest: no policy for {Org}/{App}: {Url} failed: {Message}", org, app, url, ex.Message);
        }

        return null;
    }
}
