using System.Collections.Concurrent;
using Altinn.Register.Core.RateLimiting;

namespace Altinn.Register.Localtest.Persistence;

/// <summary>In-process replacement for the Postgres rate-limit table used by the person lookup endpoint.</summary>
public sealed class InMemoryRateLimitProvider(TimeProvider timeProvider)
    : IRateLimitProvider
{
    private readonly ConcurrentDictionary<(string Policy, string Resource, string Subject), Entry> _entries = new();

    public ValueTask<RateLimitStatus> GetStatus(string policyName, string resource, string subject, BlockedRequestBehavior blockedRequestBehavior, TimeSpan blockDuration, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var key = (policyName, resource, subject);
        if (!_entries.TryGetValue(key, out var entry) || entry.WindowExpiresAt <= now && (entry.BlockedUntil is null || entry.BlockedUntil <= now))
        {
            _entries.TryRemove(key, out _);
            return ValueTask.FromResult(RateLimitStatus.NotFound);
        }

        if (entry.BlockedUntil is { } blockedUntil && blockedUntil > now && blockedRequestBehavior == BlockedRequestBehavior.Renew)
        {
            entry = entry with { BlockedUntil = now + blockDuration };
            _entries[key] = entry;
        }

        return ValueTask.FromResult(RateLimitStatus.Found(entry.Count, entry.WindowStartedAt, entry.WindowExpiresAt, entry.BlockedUntil));
    }

    public ValueTask<RateLimitStatus> Record(string policyName, string resource, string subject, ushort cost, int limit, TimeSpan windowDuration, RateLimitWindowBehavior windowBehavior, TimeSpan blockDuration, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var key = (policyName, resource, subject);
        var entry = _entries.AddOrUpdate(
            key,
            _ => new Entry(cost, now, now + windowDuration, null),
            (_, existing) =>
            {
                if (existing.WindowExpiresAt <= now && (existing.BlockedUntil is null || existing.BlockedUntil <= now))
                {
                    return new Entry(cost, now, now + windowDuration, null);
                }

                var expires = windowBehavior == RateLimitWindowBehavior.TrailingEdge ? now + windowDuration : existing.WindowExpiresAt;
                return existing with { Count = existing.Count + cost, WindowExpiresAt = expires };
            });

        if (entry.Count >= limit && entry.BlockedUntil is null)
        {
            entry = entry with { BlockedUntil = now + blockDuration };
            _entries[key] = entry;
        }

        return ValueTask.FromResult(RateLimitStatus.Found(entry.Count, entry.WindowStartedAt, entry.WindowExpiresAt, entry.BlockedUntil));
    }

    private sealed record Entry(uint Count, DateTimeOffset WindowStartedAt, DateTimeOffset WindowExpiresAt, DateTimeOffset? BlockedUntil);
}
