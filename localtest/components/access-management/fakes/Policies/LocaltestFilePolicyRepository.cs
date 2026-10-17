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
using Altinn.AccessManagement.Core.Repositories.Interfaces;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs.Models;

namespace Altinn.AccessManagement.Localtest.Policies;

/// <summary>
/// One policy blob as files: the current content at <c>{container}/{path}</c> and every written version at
/// <c>{container}/{path}.versions/{versionId}</c>. Version ids are sortable timestamps, returned to the caller the
/// way blob storage returns <c>VersionId</c>, and stored by Access Management as the delegation's policy version.
/// The PDP reads the same files through a read-only mount of the store.
/// </summary>
public sealed class LocaltestFilePolicyRepository(string containerPath, string filepath, TimeProvider timeProvider) : IPolicyRepository
{
    private static readonly SemaphoreSlim WriteLock = new(1, 1);
    private readonly string _file = Path.Combine(containerPath, filepath.Replace('/', Path.DirectorySeparatorChar));

    private string VersionsDirectory => _file + ".versions";

    public Task<Stream> GetPolicyAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Read(_file));

    public Task<Stream> GetPolicyVersionAsync(string version, CancellationToken cancellationToken = default)
        => Task.FromResult(Read(Path.Combine(VersionsDirectory, version)));

    public Task<bool> PolicyExistsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(_file));

    public async Task<Response<BlobContentInfo>> WritePolicyAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            using var buffer = new MemoryStream();
            await fileStream.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            Directory.CreateDirectory(VersionsDirectory);
            var now = timeProvider.GetUtcNow();
            var versionId = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
            await File.WriteAllBytesAsync(Path.Combine(VersionsDirectory, versionId), bytes, cancellationToken);
            await File.WriteAllBytesAsync(_file, bytes, cancellationToken);
            var info = BlobsModelFactory.BlobContentInfo(new ETag(versionId), now, null, versionId, null, null, 0);
            return Response.FromValue(info, new LocaltestResponse((int)HttpStatusCode.Created));
        }
        finally
        {
            WriteLock.Release();
        }
    }

    public Task<Response<BlobContentInfo>> WritePolicyConditionallyAsync(Stream fileStream, string blobLeaseId, CancellationToken cancellationToken = default)
        => WritePolicyAsync(fileStream, cancellationToken);

    public Task<Response> DeletePolicyVersionAsync(string version, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(VersionsDirectory, version);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.FromResult<Response>(new LocaltestResponse((int)HttpStatusCode.Accepted));
    }

    public Task<string> TryAcquireBlobLease(CancellationToken cancellationToken = default)
        => Task.FromResult(Guid.NewGuid().ToString());

    public Task ReleaseBlobLease(string leaseId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    private static Stream Read(string path)
        => File.Exists(path) ? new MemoryStream(File.ReadAllBytes(path), writable: false) : new MemoryStream();
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
