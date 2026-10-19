#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Altinn.Platform.Events.Localtest.Changes;

namespace Altinn.Platform.Events.Localtest.Inspection;

/// <summary>Fans <see cref="LocaltestChanges"/> out to every open <c>events/api/localtest/stream</c> connection.</summary>
public sealed class LocaltestEventsStream
{
    private readonly ConcurrentDictionary<Guid, Channel<LocaltestChange>> _listeners = new();

    public LocaltestEventsStream(LocaltestChanges changes)
    {
        changes.Changed += change =>
        {
            foreach (var listener in _listeners.Values)
            {
                listener.Writer.TryWrite(change);
            }
        };
    }

    public (Guid Id, ChannelReader<LocaltestChange> Reader) Listen()
    {
        var id = Guid.NewGuid();
        // A slow browser drops its oldest notifications rather than holding up the pipeline.
        var channel = Channel.CreateBounded<LocaltestChange>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });
        _listeners[id] = channel;
        return (id, channel.Reader);
    }

    public void Stop(Guid id) => _listeners.TryRemove(id, out _);
}
