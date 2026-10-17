#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Localtest.Blobs;

/// <summary>
/// File-backed <see cref="IBlobRepository"/>. A blob is the file <c>{BlobRootPath}/{org}/{blobStoragePath}</c> (the
/// org folder gets a <c>-NN</c> suffix when a storage account number is given), so the volume mirrors the Azure
/// container layout: <c>{org}/{app}/{instanceGuid}/data/{dataGuid}</c> for legacy blobs and
/// <c>{org}/{app}/{instanceGuid}/data-elements/{blobVersionId}</c> for retained blob versions. Versions are distinct
/// paths chosen by the callers, so nothing here needs to know about them.
/// </summary>
public sealed class FileBlobRepository(IOptions<LocaltestStorageOptions> options) : IBlobRepository
{
    private readonly string _root = Path.GetFullPath(options.Value.BlobRootPath);

    public async Task<(long ContentLength, DateTimeOffset LastModified)> WriteBlob(
        string org,
        Stream stream,
        string blobStoragePath,
        int? storageAccountNumber)
    {
        var path = Resolve(org, blobStoragePath, storageAccountNumber);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await stream.CopyToAsync(file);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        var info = new FileInfo(path);
        return (info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
    }

    public Task<Stream> ReadBlob(string org, string blobStoragePath, int? storageAccountNumber, CancellationToken cancellationToken = default)
    {
        var path = Resolve(org, blobStoragePath, storageAccountNumber);

        // Same contract as production: null when the blob does not exist.
        Stream result = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true)
            : null!;
        return Task.FromResult(result);
    }

    public Task<bool> DeleteBlob(string org, string blobStoragePath, int? storageAccountNumber)
        => Task.FromResult(DeleteIfExists(Resolve(org, blobStoragePath, storageAccountNumber)));

    public Task<bool[]> DeleteBlobsIfExists(
        string org,
        IReadOnlyList<string> blobStoragePaths,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default)
    {
        blobStoragePaths ??= [];
        var results = new bool[blobStoragePaths.Count];
        for (var i = 0; i < blobStoragePaths.Count; i++)
        {
            if (string.IsNullOrEmpty(blobStoragePaths[i]))
            {
                continue;
            }

            // True means deleted or already missing, as for the production batch delete (202 or 404).
            DeleteIfExists(Resolve(org, blobStoragePaths[i], storageAccountNumber));
            results[i] = true;
        }

        return Task.FromResult(results);
    }

    public Task<bool> DeleteDataBlobs(
        string org,
        string appId,
        Guid instanceGuid,
        int? storageAccountNumber,
        CancellationToken cancellationToken = default)
    {
        var folder = Resolve(org, $"{appId}/{instanceGuid}", storageAccountNumber);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        return Task.FromResult(true);
    }

    private static bool DeleteIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private string Resolve(string org, string blobStoragePath, int? storageAccountNumber)
    {
        var container = storageAccountNumber is null ? org : $"{org}-{storageAccountNumber:D2}";
        var path = Path.GetFullPath(Path.Combine(_root, container, blobStoragePath.TrimStart('/')));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Blob path escapes the blob root: {blobStoragePath}", nameof(blobStoragePath));
        }

        return path;
    }
}
