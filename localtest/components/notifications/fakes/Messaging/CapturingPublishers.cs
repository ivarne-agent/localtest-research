#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Notifications.Core.Integrations;
using Altinn.Notifications.Core.Models;
using Altinn.Notifications.Localtest.Capture;

namespace Altinn.Notifications.Localtest.Messaging;

/// <summary>
/// The three publishers the production notification services hand claimed notifications to
/// (<c>EmailCommandPublisher</c>, <c>ComposedEmailCommandPublisher</c>, <c>SendSmsCommandPublisher</c>, which put a
/// command on a Service Bus queue). These record the message in the capture and queue it for
/// <see cref="LocaltestResponder"/>, which delivers it once the claim has committed (the notification row is locked by
/// the caller's own transaction until then).
/// </summary>
public sealed class CapturingEmailPublisher(LocaltestCaptureStore store, LocaltestSendQueue queue) : IEmailCommandPublisher
{
    public async Task PublishAsync(Email email, CancellationToken cancellationToken)
    {
        await store.Capture(CapturingMessages.From(email, "queued", []), cancellationToken);
        queue.Enqueue(new SentMessage(LocaltestCaptureStore.Email, email.NotificationId));
    }
}

/// <inheritdoc cref="CapturingEmailPublisher"/>
public sealed class CapturingComposedEmailPublisher(LocaltestCaptureStore store, LocaltestSendQueue queue) : IComposedEmailCommandPublisher
{
    public async Task PublishAsync(ComposedEmail email, CancellationToken cancellationToken)
    {
        // Attachments are recorded by reference only; nothing is downloaded from the SAS URLs.
        IReadOnlyList<CapturedAttachment> attachments =
            [.. email.Attachments.Select(a => new CapturedAttachment(a.Filename, a.MimeType, a.SasUrl.ToString()))];
        await store.Capture(CapturingMessages.From(email, "composed", attachments), cancellationToken);
        queue.Enqueue(new SentMessage(LocaltestCaptureStore.Email, email.NotificationId));
    }
}

/// <inheritdoc cref="CapturingEmailPublisher"/>
public sealed class CapturingSmsPublisher(LocaltestCaptureStore store, LocaltestSendQueue queue) : ISendSmsPublisher
{
    public async Task PublishAsync(Sms sms, CancellationToken cancellationToken)
    {
        await store.Capture(
            new NewCapturedMessage(LocaltestCaptureStore.Sms, "queued", sms.NotificationId, sms.Recipient, sms.Sender, null, sms.Message, null, []),
            cancellationToken);
        queue.Enqueue(new SentMessage(LocaltestCaptureStore.Sms, sms.NotificationId));
    }
}

internal static class CapturingMessages
{
    public static NewCapturedMessage From(Email email, string kind, IReadOnlyList<CapturedAttachment> attachments)
        => new(
            LocaltestCaptureStore.Email,
            kind,
            email.NotificationId,
            email.ToAddress,
            email.FromAddress,
            email.Subject,
            email.Body,
            email.ContentType.ToString(),
            attachments);
}
