#nullable enable
using System;

namespace Altinn.Platform.Events.Localtest.Changes;

/// <summary>
/// In-process notifications of what changed in eventsdb, raised by the decorators in this folder after the production
/// repositories have written, and by the delivery log. Feeds <c>events/api/localtest/stream</c>; holds no data.
/// </summary>
public sealed class LocaltestChanges
{
    public event Action<LocaltestChange>? Changed;

    public void Raise(string entity, string kind, string? id)
        => Changed?.Invoke(new LocaltestChange("events", entity, kind, id, DateTimeOffset.UtcNow));
}

/// <summary>The shape of every inspection stream message, shared with the other localtest components.</summary>
public sealed record LocaltestChange(string Component, string Entity, string Kind, string? Id, DateTimeOffset At);
