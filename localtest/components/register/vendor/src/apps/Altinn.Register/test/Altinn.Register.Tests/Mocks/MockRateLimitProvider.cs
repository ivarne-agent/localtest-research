using System.Collections.Concurrent;
using Altinn.Register.Core.RateLimiting;

namespace Altinn.Register.Tests.Mocks;

public sealed class MockRateLimitProvider
    : IRateLimitProvider
{
    private readonly ConcurrentDictionary<Key, Entry> _entries = new();
    private readonly Lock _lock = new();

    public GetStatusCall? LastGetStatus { get; private set; }

    public RecordCall? LastRecord { get; private set; }

    public int GetStatusCallCount { get; private set; }

    public int RecordCallCount { get; private set; }

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public void SetStatus(string policyName, string resource, string subject, RateLimitStatus status)
    {
        var key = new Key(policyName, resource, subject);

        if (!status.Exists)
        {
            _entries.TryRemove(key, out _);
            return;
        }

        _entries[key] = new Entry(
            status.Count,
            status.WindowStartedAt!.Value,
            status.WindowExpiresAt!.Value,
            status.BlockedUntil);
    }

    public ValueTask<RateLimitStatus> GetStatus(
        string policyName,
        string resource,
        string subject,
        BlockedRequestBehavior blockedRequestBehavior,
        TimeSpan blockDuration,
        CancellationToken cancellationToken = default)
    {
        LastGetStatus = new(policyName, resource, subject, blockedRequestBehavior, blockDuration);
        GetStatusCallCount++;

        var now = TimeProvider.GetUtcNow();
        var key = new Key(policyName, resource, subject);

        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                return ValueTask.FromResult(RateLimitStatus.NotFound);
            }

            Entry? maybeEntry = PruneExpired(entry, now);
            if (maybeEntry is null)
            {
                _entries.TryRemove(key, out _);
                return ValueTask.FromResult(RateLimitStatus.NotFound);
            }

            entry = maybeEntry.Value;

            if (entry.BlockedUntil is not null && entry.BlockedUntil > now && blockedRequestBehavior == BlockedRequestBehavior.Renew)
            {
                entry = entry with { BlockedUntil = now + blockDuration };
            }

            _entries[key] = entry;
            return ValueTask.FromResult(ToStatus(entry));
        }
    }

    public ValueTask<RateLimitStatus> Record(
        string policyName,
        string resource,
        string subject,
        ushort cost,
        int limit,
        TimeSpan windowDuration,
        RateLimitWindowBehavior windowBehavior,
        TimeSpan blockDuration,
        CancellationToken cancellationToken = default)
    {
        LastRecord = new(policyName, resource, subject, cost, limit, windowDuration, windowBehavior, blockDuration);
        RecordCallCount++;

        var now = TimeProvider.GetUtcNow();
        var key = new Key(policyName, resource, subject);

        lock (_lock)
        {
            Entry entry;
            if (_entries.TryGetValue(key, out var existing))
            {
                Entry? maybeExisting = PruneExpired(existing, now);
                entry = maybeExisting ?? CreateNew(cost, now, windowDuration);
            }
            else
            {
                entry = CreateNew(cost, now, windowDuration);
            }

            if (entry.WindowStartedAt != now)
            {
                entry = entry with
                {
                    Count = entry.Count + cost,
                    WindowExpiresAt = windowBehavior == RateLimitWindowBehavior.TrailingEdge
                        ? now + windowDuration
                        : entry.WindowExpiresAt,
                };
            }

            if (entry.Count >= limit)
            {
                entry = entry with { BlockedUntil = now + blockDuration };
            }

            _entries[key] = entry;
            return ValueTask.FromResult(ToStatus(entry));
        }
    }

    private static Entry CreateNew(ushort cost, DateTimeOffset now, TimeSpan windowDuration)
        => new(cost, now, now + windowDuration, null);

    private static Entry? PruneExpired(Entry entry, DateTimeOffset now)
    {
        if (entry.BlockedUntil is not null && entry.BlockedUntil <= now)
        {
            entry = entry with { BlockedUntil = null };
        }

        if (entry.WindowExpiresAt <= now && entry.BlockedUntil is null)
        {
            return null;
        }

        return entry;
    }

    private static RateLimitStatus ToStatus(Entry entry)
        => RateLimitStatus.Found(
            entry.Count,
            entry.WindowStartedAt,
            entry.WindowExpiresAt,
            entry.BlockedUntil);

    private readonly record struct Key(string PolicyName, string Resource, string Subject);

    private readonly record struct Entry(
        uint Count,
        DateTimeOffset WindowStartedAt,
        DateTimeOffset WindowExpiresAt,
        DateTimeOffset? BlockedUntil);

    public readonly record struct GetStatusCall(
        string PolicyName,
        string Resource,
        string Subject,
        BlockedRequestBehavior BlockedRequestBehavior,
        TimeSpan BlockDuration);

    public readonly record struct RecordCall(
        string PolicyName,
        string Resource,
        string Subject,
        ushort Cost,
        int Limit,
        TimeSpan WindowDuration,
        RateLimitWindowBehavior WindowBehavior,
        TimeSpan BlockDuration);
}
