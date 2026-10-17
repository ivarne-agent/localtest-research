#nullable enable
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Localtest.FileScan;

/// <summary>
/// Replaces the Azure queue client. Production enqueues a scan request after the data element is committed and an
/// external scanner later calls <c>PUT dataelements/{id}/filescanstatus</c>; here the same repository update runs at
/// once with <c>Clean</c>. The update does not bump the instance version, so the upload response stays valid.
/// </summary>
public sealed class LocaltestFileScanQueueClient(IDataRepository dataRepository, ILogger<LocaltestFileScanQueueClient> logger)
    : IFileScanQueueClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task EnqueueFileScan(string content, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<FileScanRequest>(content, JsonOptions)
            ?? throw new InvalidOperationException("Empty file scan request");
        var instanceGuid = Guid.Parse(request.InstanceId[(request.InstanceId.LastIndexOf('/') + 1)..]);
        var dataElementId = Guid.Parse(request.DataElementId);

        var result = await dataRepository.UpdateFileScanStatus(
            instanceGuid,
            dataElementId,
            new FileScanStatus { FileScanResult = FileScanResult.Clean, BlobVersionId = request.BlobVersionId },
            ct);
        logger.LogInformation(
            "Localtest file scan: data element {DataElementId} marked {Result}",
            dataElementId,
            result is null ? "unchanged (missing or newer blob version)" : "Clean");
    }
}
