using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Altinn.Register.Core.RateLimiting;

/// <summary>
/// Represents the current status of a rate-limited subject.
/// </summary>
public sealed record RateLimitStatus
{
    /// <summary>
    /// Gets a singleton instance representing a subject with no stored rate-limit state.
    /// </summary>
    public static RateLimitStatus NotFound { get; } = new(
        count: 0,
        windowStartedAt: null,
        windowExpiresAt: null,
        blockedUntil: null);

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitStatus"/> class.
    /// </summary>
    private RateLimitStatus(
        uint count,
        DateTimeOffset? windowStartedAt,
        DateTimeOffset? windowExpiresAt,
        DateTimeOffset? blockedUntil)
    {
        Debug.Assert(
            (windowStartedAt is null) == (windowExpiresAt is null),
            "Window timestamps must either both be null or both have values.");
        Debug.Assert(
            blockedUntil is null || windowStartedAt is not null,
            "BlockedUntil may only have a value when window timestamps are set.");

        Count = count;
        WindowStartedAt = windowStartedAt;
        WindowExpiresAt = windowExpiresAt;
        BlockedUntil = blockedUntil;
    }

    /// <summary>
    /// Creates a <see cref="RateLimitStatus"/> for an existing rate-limit state entry.
    /// </summary>
    /// <param name="count">The accumulated count in the current window.</param>
    /// <param name="windowStartedAt">When the current window started.</param>
    /// <param name="windowExpiresAt">When the current window expires.</param>
    /// <param name="blockedUntil">When the current block expires, if blocked.</param>
    /// <returns>A <see cref="RateLimitStatus"/>.</returns>
    public static RateLimitStatus Found(
        uint count,
        DateTimeOffset windowStartedAt,
        DateTimeOffset windowExpiresAt,
        DateTimeOffset? blockedUntil)
        => new(
            count: count,
            windowStartedAt: windowStartedAt,
            windowExpiresAt: windowExpiresAt,
            blockedUntil: blockedUntil);

    /// <summary>
    /// Gets a value indicating whether the rate limiter currently has state for the specified subject.
    /// </summary>
    [MemberNotNullWhen(true, nameof(WindowStartedAt))]
    [MemberNotNullWhen(true, nameof(WindowExpiresAt))]
    public bool Exists => WindowStartedAt is not null;

    /// <summary>
    /// Gets a value indicating whether the subject is currently blocked.
    /// </summary>
    [MemberNotNullWhen(true, nameof(BlockedUntil))]
    public bool IsBlocked => BlockedUntil is not null;

    /// <summary>
    /// Gets the accumulated count in the current rate-limit window.
    /// </summary>
    public uint Count { get; }

    /// <summary>
    /// Gets when the current window started, if state exists.
    /// </summary>
    public DateTimeOffset? WindowStartedAt { get; }

    /// <summary>
    /// Gets when the current window expires, if state exists.
    /// </summary>
    public DateTimeOffset? WindowExpiresAt { get; }

    /// <summary>
    /// Gets when the current block expires, if the subject is blocked.
    /// </summary>
    public DateTimeOffset? BlockedUntil { get; }
}
