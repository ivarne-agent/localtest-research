#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Altinn.ResourceRegistry.Core;
using Altinn.ResourceRegistry.Core.Extensions;
using Altinn.ResourceRegistry.Core.Helpers;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace Altinn.ResourceRegistry.Localtest.Policies;

/// <summary>
/// The two policy blob containers as folders on the policy store shared with the PDP and Access Management: app
/// policies in <c>metadata/{org}/{app}/policy.xml</c> (exactly where the PDP's policy endpoint writes the policies
/// the app runner pushes) and resource policies in <c>resourceregistry/{id}/resourcepolicy.xml</c> (upstream's blob
/// name). Every written version is kept at <c>{file}.versions/{versionId}</c>, with the sortable timestamp as version id
/// that the file repositories of the PDP and Access Management use. A blob lease is a process-wide lock.
/// </summary>
public sealed class LocaltestFilePolicyRepository(IOptions<LocaltestResourceRegistryOptions> options, TimeProvider timeProvider) : IPolicyRepository
{
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    public Task<Stream?> GetPolicyAsync(string resourceId, CancellationToken cancellationToken = default)
        => Task.FromResult(Read(ResourceFile(resourceId)));

    public Task<Stream?> GetAppPolicyAsync(string org, string app, CancellationToken cancellationToken = default)
        => Task.FromResult(Read(AppFile(org, app)));

    public Task<Stream?> GetPolicyVersionAsync(string resourceId, string version, CancellationToken cancellationToken = default)
        => Task.FromResult(IsSafeVersion(version) ? Read(Path.Combine(ResourceFile(resourceId) + ".versions", version)) : null);

    public Task<Response<BlobContentInfo>> WritePolicyAsync(string resourceId, Stream fileStream, CancellationToken cancellationToken = default)
        => Write(ResourceFile(resourceId), fileStream, cancellationToken);

    public Task<Response<BlobContentInfo>> WriteAppPolicyAsync(string org, string app, Stream fileStream, CancellationToken cancellationToken = default)
        => Write(AppFile(org, app), fileStream, cancellationToken);

    public Task<Response<BlobContentInfo>> WritePolicyConditionallyAsync(string resourceId, Stream fileStream, string blobLeaseId, CancellationToken cancellationToken = default)
        => WritePolicyAsync(resourceId, fileStream, cancellationToken);

    public Task<Response> DeletePolicyVersionAsync(string resourceId, string version, CancellationToken cancellationToken = default)
    {
        if (IsSafeVersion(version))
        {
            File.Delete(Path.Combine(ResourceFile(resourceId) + ".versions", version));
        }

        return Task.FromResult<Response>(new LocaltestResponse((int)HttpStatusCode.Accepted));
    }

    public async Task<bool> TryDeletePolicyAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            var file = ResourceFile(resourceId);
            File.Delete(file);
            if (Directory.Exists(file + ".versions"))
            {
                Directory.Delete(file + ".versions", recursive: true);
            }

            return true;
        }
        finally
        {
            WriteLock.Release();
        }
    }

    public Task<string> TryAcquireBlobLease(string resourceId, CancellationToken cancellationToken = default)
        => Task.FromResult(Guid.NewGuid().ToString());

    public Task ReleaseBlobLease(string resourceId, string leaseId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<bool> PolicyExistsAsync(string resourceId, CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(ResourceFile(resourceId)));

    private string ResourceFile(string resourceId)
        => Within("resourceregistry", $"{resourceId.AsFilePath()}/resourcepolicy.xml");

    private string AppFile(string org, string app)
        => Within("metadata", PolicyHelper.GetAltinnAppsPolicyPath(org, app));

    // The container folder plus the blob name; a name that would leave the container resolves to a file that does not exist.
    private string Within(string container, string blobName)
    {
        var root = Path.GetFullPath(Path.Combine(options.Value.PolicyStorePath, container));
        var file = Path.GetFullPath(Path.Combine(root, blobName.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar)));
        return file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? file : Path.Combine(root, "invalid-path");
    }

    private async Task<Response<BlobContentInfo>> Write(string file, Stream fileStream, CancellationToken cancellationToken)
    {
        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            using var buffer = new MemoryStream();
            await fileStream.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            Directory.CreateDirectory(file + ".versions");
            var now = timeProvider.GetUtcNow();
            var versionId = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
            await File.WriteAllBytesAsync(Path.Combine(file + ".versions", versionId), bytes, cancellationToken);
            await File.WriteAllBytesAsync(file, bytes, cancellationToken);
            var info = BlobsModelFactory.BlobContentInfo(new ETag(versionId), now, null, versionId, null, null, 0);
            return Response.FromValue(info, new LocaltestResponse((int)HttpStatusCode.Created));
        }
        finally
        {
            WriteLock.Release();
        }
    }

    private static bool IsSafeVersion(string version)
        => !string.IsNullOrWhiteSpace(version) && version.IndexOfAny(['/', '\\']) < 0 && version != "." && version != "..";

    // Upstream returns null for a missing or empty blob.
    private static Stream? Read(string file)
    {
        if (!File.Exists(file))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(file);
        return bytes.Length == 0 ? null : new MemoryStream(bytes, writable: false);
    }
}

internal sealed class LocaltestResponse(int status) : Response
{
    public override int Status => status;

    public override string ReasonPhrase => string.Empty;

    public override Stream? ContentStream { get; set; }

    public override string ClientRequestId { get; set; } = Guid.NewGuid().ToString();

    public override void Dispose()
    {
    }

    protected override bool ContainsHeader(string name) => false;

    protected override IEnumerable<HttpHeader> EnumerateHeaders() => Enumerable.Empty<HttpHeader>();

    protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
    {
        value = null;
        return false;
    }

    protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
    {
        values = null;
        return false;
    }
}
