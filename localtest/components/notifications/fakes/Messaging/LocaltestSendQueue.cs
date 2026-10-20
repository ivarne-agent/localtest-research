#nullable enable
using System;
using System.Threading.Channels;

namespace Altinn.Notifications.Localtest.Messaging;

/// <summary>
/// The in-process stand-in for the Service Bus queues between the orders host and the Email and SMS hosts: the capturing
/// publishers write here, <see cref="LocaltestResponder"/> reads. A separate object so the publishers (which the
/// production notification services depend on) and the responder (which depends on those services) have no cycle.
/// </summary>
public sealed class LocaltestSendQueue
{
    private readonly Channel<SentMessage> _channel = Channel.CreateUnbounded<SentMessage>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<SentMessage> Reader => _channel.Reader;

    public void Enqueue(SentMessage message) => _channel.Writer.TryWrite(message);
}

/// <summary>A captured message waiting for its (always successful) delivery. <c>Channel</c> is <c>email</c> or <c>sms</c>.</summary>
public sealed record SentMessage(string Channel, Guid NotificationId);
