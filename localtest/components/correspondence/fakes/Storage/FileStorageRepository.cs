using System.Security.Cryptography;
using System.Text.Json;
using Altinn.Correspondence.Core.Exceptions;
using Altinn.Correspondence.Core.Models.Entities;
using Altinn.Correspondence.Core.Repositories;
using Azure;
using Microsoft.Extensions.Options;

namespace Altinn.Correspondence.Localtest.Storage;

/// <summary>
/// <see cref="IStorageRepository"/> on a directory (the <c>correspondence-blobs</c> volume) instead of Azure Storage:
/// attachments at <c>{BlobRootPath}/attachments/{id}</c>, the daily reports at <c>{BlobRootPath}/reports/{name}</c> with
/// their two count tags and hash in <c>{name}.meta.json</c>. There is one store: production's per-service-owner storage
/// accounts (<see cref="StorageProviderEntity"/>) are ignored, as is the default account's connection string. The
/// observable behaviour follows production's <c>StorageRepository</c>: the MD5 of the uploaded stream as lower-case hex,
/// <see cref="HashMismatchException"/> and no blob left behind when the attachment's checksum differs, and
/// <see cref="RequestFailedException"/> (404) for a missing blob. The location URL is the anonymous inspection route
/// <c>{PlatformEndpoint}correspondence/api/localtest/blobs/{id}</c>, which ends with the id as production's blob URIs do;
/// it is also what <see cref="GenerateSasUrl"/> answers (composed e-mail attachments).
/// </summary>
public sealed class FileStorageRepository(IOptions<LocaltestCorrespondenceOptions> options, ILogger<FileStorageRepository> logger) : IStorageRepository
{
    private const string MetaSuffix = ".meta.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string Root => options.Value.BlobRootPath;

    public string AttachmentPath(Guid attachmentId) => Path.Combine(Root, "attachments", attachmentId.ToString("D"));

    public string BlobUrl(Guid attachmentId)
        => $"{options.Value.PlatformEndpoint.TrimEnd('/')}/correspondence/api/localtest/blobs/{attachmentId:D}";

    public async Task<(string locationUrl, string hash, long size)> UploadAttachment(
        AttachmentEntity attachment, Stream stream, StorageProviderEntity? storageProviderEntity, CancellationToken cancellationToken)
    {
        var path = AttachmentPath(attachment.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.uploading";
        try
        {
            using var md5 = MD5.Create();
            long size;
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            await using (var hashing = new CryptoStream(file, md5, CryptoStreamMode.Write, leaveOpen: true))
            {
                await stream.CopyToAsync(hashing, cancellationToken);
                await hashing.FlushFinalBlockAsync(cancellationToken);
                size = file.Length;
            }

            var hash = Convert.ToHexStringLower(md5.Hash!);
            if (!string.IsNullOrWhiteSpace(attachment.Checksum) && !string.Equals(hash, attachment.Checksum, StringComparison.InvariantCultureIgnoreCase))
            {
                throw new HashMismatchException("Hash mismatch");
            }

            File.Move(temporary, path, overwrite: true);
            logger.LogInformation("Localtest: stored attachment {AttachmentId} ({Size} bytes) at {Path}", attachment.Id, size, path);
            return (BlobUrl(attachment.Id), hash, size);
        }
        catch (Exception ex)
        {
            // As production: log, remove what was written, rethrow (the handler maps HashMismatchException and
            // RequestFailedException to the attachment's Failed status).
            logger.LogError("Error occurred while uploading file: {errorMessage}", ex.Message);
            File.Delete(temporary);
            throw;
        }
    }

    public Task<Stream> DownloadAttachment(Guid attachmentId, StorageProviderEntity? storageProviderEntity, CancellationToken cancellationToken)
    {
        var path = AttachmentPath(attachmentId);
        if (!File.Exists(path))
        {
            throw BlobNotFound(attachmentId.ToString("D"));
        }

        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024, useAsync: true));
    }

    public Task PurgeAttachment(Guid attachmentId, StorageProviderEntity? storageProviderEntity, CancellationToken cancellationToken)
    {
        File.Delete(AttachmentPath(attachmentId));
        return Task.CompletedTask;
    }

    public async Task<(string locationUrl, string hash, long size)> UploadReportFile(
        string fileName, int serviceOwnerCount, int correspondenceCount, Stream stream, CancellationToken cancellationToken)
    {
        var path = ReportPath(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // As production: the MD5 of the stream, base64 in the answer, stored as the blob's content hash.
        var md5 = await MD5.HashDataAsync(stream, cancellationToken);
        stream.Position = 0;
        await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
        {
            await stream.CopyToAsync(file, cancellationToken);
        }

        var meta = new ReportMeta(serviceOwnerCount, correspondenceCount, Convert.ToHexStringLower(md5));
        await File.WriteAllTextAsync(path + MetaSuffix, JsonSerializer.Serialize(meta, Json), cancellationToken);
        return ($"{options.Value.PlatformEndpoint.TrimEnd('/')}/correspondence/api/localtest/reports/{Uri.EscapeDataString(fileName)}",
            Convert.ToBase64String(md5), stream.Length);
    }

    public async Task<(Stream DownloadStream, string FileName, long FileSize, string FileHash, int ServiceOwnerCount, int CorrespondenceCount)> DownloadReportFile(
        string fileName, CancellationToken cancellationToken)
    {
        var path = ReportPath(fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Report file '{fileName}' not found in blob storage");
        }

        var meta = File.Exists(path + MetaSuffix)
            ? JsonSerializer.Deserialize<ReportMeta>(await File.ReadAllTextAsync(path + MetaSuffix, cancellationToken), Json)
            : null;
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024, useAsync: true);
        return (stream, fileName, stream.Length, meta?.Hash ?? string.Empty, meta?.ServiceOwnerCount ?? 0, meta?.CorrespondenceCount ?? 0);
    }

    public Task<(Stream DownloadStream, string FileName, long FileSize, string FileHash, int ServiceOwnerCount, int CorrespondenceCount)> DownloadLatestReportFile(
        CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(Path.Combine(Root, "reports"));
        var latest = directory.Exists
            ? directory.EnumerateFiles().Where(f => !f.Name.EndsWith(MetaSuffix, StringComparison.Ordinal)).MaxBy(f => f.LastWriteTimeUtc)
            : null;
        if (latest is null)
        {
            throw new FileNotFoundException("No report files found in blob storage");
        }

        return DownloadReportFile(latest.Name, cancellationToken);
    }

    public Task<string> GenerateSasUrl(AttachmentEntity attachment, StorageProviderEntity? storageProviderEntity, CancellationToken cancellationToken)
        => Task.FromResult(BlobUrl(attachment.Id));

    private string ReportPath(string fileName)
    {
        // Report names are generated by the report handler; refuse anything that would leave the directory.
        if (fileName.Length == 0 || fileName != Path.GetFileName(fileName))
        {
            throw new FileNotFoundException($"Report file '{fileName}' not found in blob storage");
        }

        return Path.Combine(Root, "reports", fileName);
    }

    private static RequestFailedException BlobNotFound(string name)
        => new(404, $"The specified blob does not exist: {name}", "BlobNotFound", null);

    private sealed record ReportMeta(int ServiceOwnerCount, int CorrespondenceCount, string Hash);
}
