#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Localtest.Dialogporten;

/// <summary>
/// Posts an outbox command to the Dialogporten adapter's anonymous <c>api/localtest/enqueue</c> endpoint, which puts it
/// on the adapter's own queue (the adapter's handler and retry policies run there, as for a Service Bus message).
/// <para>
/// Storage sends whether or not the adapter runs. When it does not (the optional <c>dialogporten</c> group is off), the
/// front door answers its disabled route with 503 problem details; a refused connection, 502, 504, or a 404 (no route
/// claims the path, so it reaches Storage itself under the longest matching prefix <c>/storage</c>) count the same:
/// that is "not enabled", logged once per command and dropped. Nothing is retried: the outbox row is gone once the
/// command is on the local queue, and a later change of the instance sends a new one. Any other answer is a warning,
/// also dropped. The handler never throws, so Wolverine never retries it.
/// </para>
/// </summary>
public sealed partial class LocaltestDialogportenAdapterClient(
    IHttpClientFactory httpClientFactory,
    IOptions<LocaltestStorageOptions> options,
    ILogger<LocaltestDialogportenAdapterClient> logger
)
{
    /// <summary>The named client (30 s timeout). A singleton by type with a plain constructor, so Wolverine's generated
    /// handler code builds it without service location.</summary>
    public const string HttpClientName = "localtest-dialogporten-adapter";

    public async Task Enqueue(SyncInstanceToDialogportenCommand command, CancellationToken cancellationToken)
    {
        var url = options.Value.DialogportenAdapterEndpoint.TrimEnd('/') + "/api/localtest/enqueue";

        // The adapter's SyncInstanceCommand: the same fields without the event type. Npgsql reads the timestamp as UTC.
        var body = new
        {
            command.AppId,
            command.PartyId,
            command.InstanceId,
            InstanceCreatedAt = DateTime.SpecifyKind(command.InstanceCreatedAt, DateTimeKind.Utc),
            command.IsMigration,
        };

        HttpResponseMessage response;
        try
        {
            using var httpClient = httpClientFactory.CreateClient(HttpClientName);
            response = await httpClient.PostAsJsonAsync(url, body, cancellationToken);
        }
        catch (HttpRequestException e)
        {
            LogNotRunning(logger, command.PartyId, command.InstanceId, e.Message);
            return;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogNotRunning(logger, command.PartyId, command.InstanceId, "timed out");
            return;
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                LogEnqueued(logger, command.PartyId, command.InstanceId, command.EventType.ToString());
                return;
            }

            if (
                response.StatusCode
                is HttpStatusCode.NotFound
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.GatewayTimeout
            )
            {
                LogNotRunning(logger, command.PartyId, command.InstanceId, $"{(int)response.StatusCode} from {url}");
                return;
            }

            var answer = await response.Content.ReadAsStringAsync(cancellationToken);
            LogRefused(logger, command.PartyId, command.InstanceId, (int)response.StatusCode, answer);
        }
    }

    [LoggerMessage(
        LogLevel.Information,
        "Dialogporten sync of instance {PartyId}/{InstanceId} dropped: the Dialogporten adapter is not running (the dialogporten group is off?): {Reason}"
    )]
    private static partial void LogNotRunning(ILogger logger, string partyId, string instanceId, string reason);

    [LoggerMessage(
        LogLevel.Information,
        "Dialogporten sync of instance {PartyId}/{InstanceId} handed to the adapter ({EventType})"
    )]
    private static partial void LogEnqueued(ILogger logger, string partyId, string instanceId, string eventType);

    [LoggerMessage(
        LogLevel.Warning,
        "Dialogporten sync of instance {PartyId}/{InstanceId} dropped: the adapter answered {StatusCode}: {Answer}"
    )]
    private static partial void LogRefused(ILogger logger, string partyId, string instanceId, int statusCode, string answer);
}
