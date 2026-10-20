#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Notifications.Localtest.Capture;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Notifications.Localtest.Inspection;

/// <summary>
/// What a tester sees of the capture, as inboxes: one per e-mail address or phone number, holding the messages sent to
/// it. Anonymous, like the other components' <c>api/localtest</c>. The production orders and notifications are read
/// through the production API; this is only the capture.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("notifications/api/localtest")]
public sealed class LocaltestNotificationsController(LocaltestCaptureStore store) : ControllerBase
{
    private const int DefaultLimit = 200;
    private const int MaxLimit = 1000;

    /// <summary>Every inbox with its message count. <c>receiving</c> is always true in this version.</summary>
    [HttpGet("inboxes")]
    public async Task<ActionResult<InboxList>> Inboxes(CancellationToken cancellationToken)
        => Ok(new InboxList([.. (await store.ListInboxes(cancellationToken)).Select(InboxDto.From)]));

    /// <summary>
    /// One inbox and its messages in capture order. The address is the exact string messages were addressed to (an
    /// e-mail address or a phone number as given; escape <c>+</c> as <c>%2B</c> if a client turns it into a space).
    /// </summary>
    [HttpGet("inboxes/{address}")]
    public async Task<ActionResult<InboxWithMessages>> Inbox(string address, CancellationToken cancellationToken)
    {
        var key = LocaltestCaptureStore.NormalizeAddress(LocaltestCaptureStore.ChannelOf(address), address);
        if (await store.GetInbox(key, cancellationToken) is not { } inbox)
        {
            return NotFound();
        }

        var messages = await store.Messages(new MessageFilter(null, key, null, 0, MaxLimit), cancellationToken);
        return Ok(new InboxWithMessages(InboxDto.From(inbox), [.. messages.Select(MessageDto.From)]));
    }

    /// <summary>
    /// The captured messages in capture order, optionally those of one order (<c>orderId</c>: the id a v1 order answers,
    /// the <c>shipmentId</c> of a <c>future/orders</c> order, or the <c>notificationOrderId</c> of the order chain), one channel (<c>email</c> or <c>sms</c>), after a message id (<c>after</c>, for
    /// polling) and at most <c>limit</c> (default 200).
    /// </summary>
    [HttpGet("messages")]
    public async Task<ActionResult<MessageList>> Messages(
        [FromQuery] Guid? orderId,
        [FromQuery] string? channel,
        [FromQuery] long after = 0,
        [FromQuery] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (channel is not (null or LocaltestCaptureStore.Email or LocaltestCaptureStore.Sms))
        {
            return BadRequest("channel must be email or sms.");
        }

        var messages = await store.Messages(
            new MessageFilter(orderId, null, channel, Math.Max(after, 0), Math.Clamp(limit, 1, MaxLimit)),
            cancellationToken);
        return Ok(new MessageList([.. messages.Select(MessageDto.From)], messages.Count > 0 ? messages[^1].Id : after));
    }

    /// <summary>Empties the capture (every message). The inboxes stay; the production orders and notifications are not touched.</summary>
    [HttpDelete("messages")]
    public async Task<IActionResult> DeleteMessages(CancellationToken cancellationToken)
    {
        await store.Reset(cancellationToken);
        return NoContent();
    }

    public sealed record InboxList(IReadOnlyList<InboxDto> Items);

    public sealed record MessageList(IReadOnlyList<MessageDto> Items, long Next);

    public sealed record InboxWithMessages(InboxDto Inbox, IReadOnlyList<MessageDto> Messages);

    public sealed record InboxDto(string Address, string Channel, bool Receiving, long MessageCount, DateTimeOffset CreatedAt, DateTimeOffset? LastMessageAt)
    {
        public static InboxDto From(InboxSummary inbox)
            => new(inbox.Address, inbox.Channel, inbox.Receiving, inbox.MessageCount, inbox.CreatedAt, inbox.LastMessageAt);
    }

    public sealed record MessageDto(
        long Id,
        string Channel,
        string Kind,
        string Address,
        string Recipient,
        string? Sender,
        string? Subject,
        string Body,
        string? ContentType,
        IReadOnlyList<CapturedAttachment> Attachments,
        Guid? OrderId,
        Guid? OrderChainId,
        Guid NotificationId,
        string? Creator,
        string? SendersReference,
        DateTimeOffset CapturedAt,
        string? Outcome,
        DateTimeOffset? OutcomeAt)
    {
        public static MessageDto From(CapturedMessage m)
            => new(
                m.Id,
                m.Channel,
                m.Kind,
                m.Address,
                m.Recipient,
                m.Sender,
                m.Subject,
                m.Body,
                m.ContentType,
                m.Attachments,
                m.OrderId,
                m.OrderChainId,
                m.NotificationId,
                m.Creator,
                m.SendersReference,
                m.CapturedAt,
                m.Outcome,
                m.OutcomeAt);
    }
}
