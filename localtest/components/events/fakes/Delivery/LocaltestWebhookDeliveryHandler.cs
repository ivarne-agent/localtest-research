#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Events.Localtest.Delivery;

/// <summary>
/// Sits on the production named client <c>WebhookService</c>, which sends every event delivery and every subscription
/// validation. Records each POST, and either lets it through or answers it
/// itself with a synthetic <c>200</c> (header <c>X-Localtest-Captured: true</c>), so no event leaves the stack. The
/// production <c>WebhookService</c>, its trace log entries and the retry handling see an ordinary response.
/// Records go to <c>localtest.delivery</c> in eventsdb through <see cref="LocaltestDeliveryLog"/>.
/// </summary>
public sealed class LocaltestWebhookDeliveryHandler(LocaltestDeliveryLog log, IOptions<LocaltestEventsOptions> options) : DelegatingHandler
{
    private const int MaxRecordedResponseBody = 4096;
    private const string ValidationType = "platform.events.validatesubscription";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(h => h.Key, h => h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ? "(redacted)" : string.Join(", ", h.Value));
        (string? eventId, string? eventType) = ReadEvent(body);
        string endpoint = request.RequestUri?.ToString() ?? string.Empty;
        bool deliver = ShouldDeliver(request.RequestUri);
        var stopwatch = Stopwatch.StartNew();

        DeliveryRecord Record(int? status, string? responseBody, string? error) => new(
            eventId,
            eventType,
            eventType == ValidationType ? "validation" : "event",
            endpoint,
            deliver ? "delivered" : "captured",
            headers,
            body,
            status,
            responseBody,
            stopwatch.ElapsedMilliseconds,
            error);

        if (!deliver)
        {
            await log.Record(Record((int)HttpStatusCode.OK, null, null), cancellationToken);
            var captured = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(string.Empty) };
            captured.Headers.Add("X-Localtest-Captured", "true");
            return captured;
        }

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception e)
        {
            await log.Record(Record(null, null, $"{e.GetType().Name}: {e.Message}"), CancellationToken.None);
            throw;
        }

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (responseBody.Length > MaxRecordedResponseBody)
        {
            responseBody = responseBody[..MaxRecordedResponseBody];
        }

        await log.Record(Record((int)response.StatusCode, responseBody, null), cancellationToken);
        return response;
    }

    private bool ShouldDeliver(Uri? uri)
    {
        switch (options.Value.Delivery)
        {
            case WebhookDelivery.All:
                return true;
            case WebhookDelivery.CaptureAll:
                return false;
        }

        if (uri is null)
        {
            return false;
        }

        string platformHost = new Uri(options.Value.PlatformEndpoint).Host;
        string host = uri.Host;
        return !host.Contains('.')
            || host.Equals(platformHost, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + platformHost, StringComparison.OrdinalIgnoreCase);
    }

    private static (string? Id, string? Type) ReadEvent(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            // Slack endpoints get the event wrapped as { "text": ... }; everything else gets the CloudEvent itself.
            return (
                root.TryGetProperty("id", out var id) ? id.GetString() : null,
                root.TryGetProperty("type", out var type) ? type.GetString() : null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
