#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authorization.Repositories.Interface;
using Azure;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Authorization.Localtest.Policies;

/// <summary>
/// Replaces the Azure Blob policy store with the file store shared with the Access Management component (the
/// <c>policy-store</c> volume), laid out as the blob containers are: <c>metadata/{org}/{app}/policy.xml</c> for app
/// policies and <c>delegationpolicies/{path}</c> for delegation policies, each with its written versions under
/// <c>{path}.versions/{versionId}</c>. App policies arrive through the production <c>POST authorization/api/v1/policies</c>
/// (the app runner pushes them, placeholders already replaced, as Designer does); delegation policies are written by
/// Access Management and read here by the version id it recorded. Nothing is fetched from apps.
/// </summary>
public sealed class LocaltestPolicyRepository(
    IOptions<LocaltestAuthorizationOptions> options,
    TimeProvider timeProvider,
    ILogger<LocaltestPolicyRepository> logger)
    : IPolicyRepository
{
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    public Task<Stream> GetPolicyAsync(string filepath, CancellationToken cancellationToken = default)
        => Task.FromResult(Read(FileFor(filepath)));

    public Task<Stream> GetPolicyVersionAsync(string filepath, string version, CancellationToken cancellationToken = default)
        => Task.FromResult(IsSafeVersion(version) ? Read(Path.Combine(VersionsDirectory(filepath), version)) : new MemoryStream());

    public async Task<Response<BlobContentInfo>> WritePolicyAsync(string filepath, Stream fileStream, CancellationToken cancellationToken = default)
    {
        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            using var buffer = new MemoryStream();
            await fileStream.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            var file = FileFor(filepath);
            Directory.CreateDirectory(VersionsDirectory(filepath));
            var now = timeProvider.GetUtcNow();
            var versionId = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
            await File.WriteAllBytesAsync(Path.Combine(VersionsDirectory(filepath), versionId), bytes, cancellationToken);
            await File.WriteAllBytesAsync(file, bytes, cancellationToken);
            logger.LogInformation("Localtest: wrote policy {Path} ({Bytes} bytes, version {Version})", filepath, bytes.Length, versionId);
            var info = BlobsModelFactory.BlobContentInfo(new ETag(versionId), now, null, versionId, null, null, 0);
            return Response.FromValue(info, new LocaltestResponse((int)HttpStatusCode.Created));
        }
        finally
        {
            WriteLock.Release();
        }
    }

    public Task<Response<BlobContentInfo>> WritePolicyConditionallyAsync(string filepath, Stream fileStream, string blobLeaseId, CancellationToken cancellationToken = default)
        => WritePolicyAsync(filepath, fileStream, cancellationToken);

    public Task<Response> DeletePolicyVersionAsync(string filepath, string version, CancellationToken cancellationToken = default)
    {
        if (IsSafeVersion(version))
        {
            File.Delete(Path.Combine(VersionsDirectory(filepath), version));
        }

        return Task.FromResult<Response>(new LocaltestResponse((int)HttpStatusCode.Accepted));
    }

    public Task<string> TryAcquireBlobLease(string filepath, CancellationToken cancellationToken = default)
        => Task.FromResult(Guid.NewGuid().ToString());

    public Task ReleaseBlobLease(string filepath, string leaseId) => Task.CompletedTask;

    public Task<bool> PolicyExistsAsync(string filepath, CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(FileFor(filepath)));

    // Production: PolicyRepository.CreateBlobClient picks the delegations container for delegation policies.
    private string FileFor(string filepath)
    {
        var container = filepath.Contains("delegationpolicy.xml", StringComparison.Ordinal) ? "delegationpolicies" : "metadata";
        var relative = filepath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(Path.Combine(options.Value.PolicyStorePath, container));
        var file = Path.GetFullPath(Path.Combine(root, relative));
        return file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? file : Path.Combine(root, "invalid-path");
    }

    private string VersionsDirectory(string filepath) => FileFor(filepath) + ".versions";

    private static bool IsSafeVersion(string version)
        => !string.IsNullOrWhiteSpace(version) && version.IndexOfAny(['/', '\\']) < 0 && version != "." && version != "..";

    private static Stream Read(string file)
        => File.Exists(file) ? new MemoryStream(File.ReadAllBytes(file), writable: false) : new MemoryStream();
}
