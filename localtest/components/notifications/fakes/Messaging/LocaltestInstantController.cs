#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Altinn.Notifications.Core.Models.InstantEmailService;
using Altinn.Notifications.Core.Models.ShortMessageService;
using Altinn.Notifications.Localtest.Capture;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Notifications.Localtest.Messaging;

/// <summary>
/// The two HTTP endpoints of the Email and SMS hosts that instant orders call
/// (<c>notifications/email/api/v1/instantemail</c>, <c>notifications/sms/api/v1/instantmessage/send</c>). The production
/// instant clients and service run unchanged and are pointed at this host (<c>PlatformSettings:ApiInstantEmailServiceEndpoint</c>,
/// <c>ApiShortMessageServiceEndpoint</c>); the message is captured and delivered like the queued ones. The order and its
/// notification are written by the production code after this call returns, which is why the responder waits for the row.
/// Anonymous, like the hosts behind the platform.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class LocaltestInstantController(LocaltestCaptureStore store, LocaltestSendQueue queue) : ControllerBase
{
    [HttpPost("notifications/email/api/v1/instantemail")]
    public async Task<IActionResult> InstantEmail([FromBody] InstantEmail email, CancellationToken cancellationToken)
    {
        await store.Capture(
            new NewCapturedMessage(
                LocaltestCaptureStore.Email,
                "instant",
                email.NotificationId,
                email.Recipient,
                email.Sender,
                email.Subject,
                email.Body,
                email.ContentType.ToString(),
                []),
            cancellationToken);
        queue.Enqueue(new SentMessage(LocaltestCaptureStore.Email, email.NotificationId));
        return Accepted();
    }

    [HttpPost("notifications/sms/api/v1/instantmessage/send")]
    public async Task<IActionResult> InstantSms([FromBody] ShortMessage message, CancellationToken cancellationToken)
    {
        await store.Capture(
            new NewCapturedMessage(LocaltestCaptureStore.Sms, "instant", message.NotificationId, message.Recipient, message.Sender, null, message.Message, null, []),
            cancellationToken);
        queue.Enqueue(new SentMessage(LocaltestCaptureStore.Sms, message.NotificationId));
        return Ok();
    }
}
