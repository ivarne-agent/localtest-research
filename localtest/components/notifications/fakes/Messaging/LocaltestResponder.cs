#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Notifications.Core.Enums;
using Altinn.Notifications.Core.Models.Notification;
using Altinn.Notifications.Core.Services.Interfaces;
using Altinn.Notifications.Localtest.Capture;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Notifications.Localtest.Messaging;

/// <summary>
/// Plays the Email and SMS hosts and the providers behind the queue: every captured message is delivered. For each one it
/// does what the production result and delivery report handlers do (<c>EmailSendResultHandler</c>,
/// <c>EmailDeliveryReportHandler</c>, <c>SmsSendResultHandler</c>, <c>SmsDeliveryReportHandler</c>, each a parse followed
/// by <c>UpdateSendStatus</c> on the production service): first <c>Succeeded</c> (e-mail, with an operation id) or
/// <c>Accepted</c> (SMS, with a gateway reference), then <c>Delivered</c> addressed by that operation id or gateway
/// reference alone, as a delivery report is.
/// <para>
/// The ordering is the point of this class. The publisher is called between the claim and the commit of the claim's
/// transaction (<c>SendClaimedNotification</c>), so the notification row is still locked by the caller's own transaction;
/// production never meets that because the result comes back through the queue. Here the result waits until the
/// committed row has left <c>New</c> (the claim function sets <c>Sending</c>; an instant order's row is written as
/// <c>Sending</c> after the post), which no uncommitted transaction can show, and only then applies the results. Every
/// message is handled on its own task, so one that never commits holds nobody up.
/// </para>
/// </summary>
public sealed class LocaltestResponder(
    LocaltestSendQueue queue,
    LocaltestCaptureStore store,
    IEmailNotificationService emailService,
    ISmsNotificationService smsService,
    IOptions<LocaltestNotificationsOptions> options,
    ILogger<LocaltestResponder> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            _ = Task.Run(() => Deliver(message, stoppingToken), CancellationToken.None);
        }
    }

    private async Task Deliver(SentMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await WaitForCommittedClaim(message, cancellationToken);
            await Task.Delay(options.Value.ResultDelayMs, cancellationToken);
            var reference = "localtest-" + message.NotificationId.ToString("N");

            if (message.Channel == LocaltestCaptureStore.Email)
            {
                await emailService.UpdateSendStatus(new EmailSendOperationResult
                {
                    NotificationId = message.NotificationId,
                    OperationId = reference,
                    SendResult = EmailNotificationResultType.Succeeded,
                });
                await Task.Delay(options.Value.DeliveryReportDelayMs, cancellationToken);
                await emailService.UpdateSendStatus(new EmailSendOperationResult
                {
                    OperationId = reference,
                    SendResult = EmailNotificationResultType.Delivered,
                });
            }
            else
            {
                await smsService.UpdateSendStatus(new SmsSendOperationResult
                {
                    NotificationId = message.NotificationId,
                    GatewayReference = reference,
                    SendResult = SmsNotificationResultType.Accepted,
                });
                await Task.Delay(options.Value.DeliveryReportDelayMs, cancellationToken);
                await smsService.UpdateSendStatus(new SmsSendOperationResult
                {
                    GatewayReference = reference,
                    SendResult = SmsNotificationResultType.Delivered,
                });
            }

            logger.LogInformation("Localtest: delivered {Channel} notification {NotificationId}", message.Channel, message.NotificationId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Localtest: could not deliver {Channel} notification {NotificationId}", message.Channel, message.NotificationId);
        }
    }

    private async Task WaitForCommittedClaim(SentMessage message, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.Value.ClaimCommitTimeoutSeconds);
        while (true)
        {
            var result = await store.NotificationResult(message.Channel, message.NotificationId, cancellationToken);
            if (result is not null && result != "New")
            {
                return;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                logger.LogWarning(
                    "Localtest: {Channel} notification {NotificationId} still {Result} after {Seconds}s; applying the result anyway",
                    message.Channel,
                    message.NotificationId,
                    result ?? "missing",
                    options.Value.ClaimCommitTimeoutSeconds);
                return;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}
