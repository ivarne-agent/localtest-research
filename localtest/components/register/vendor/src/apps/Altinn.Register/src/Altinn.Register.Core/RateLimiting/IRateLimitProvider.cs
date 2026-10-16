namespace Altinn.Register.Core.RateLimiting;

/// <summary>
/// Service for checking and recording rate-limit state for a subject.
/// </summary>
public interface IRateLimitProvider
{
    /// <summary>
    /// Gets the current status for a rate-limited subject.
    /// </summary>
    /// <param name="policyName">The rate-limit policy name.</param>
    /// <param name="resource">The resource identifier within the policy.</param>
    /// <param name="subject">The subject identifier within the policy.</param>
    /// <param name="blockedRequestBehavior">How requests should be handled while the subject is already blocked.</param>
    /// <param name="blockDuration">How long a subject remains blocked after the rate limit has been exceeded.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The current <see cref="RateLimitStatus"/>.</returns>
    public ValueTask<RateLimitStatus> GetStatus(
        string policyName,
        string resource,
        string subject,
        BlockedRequestBehavior blockedRequestBehavior,
        TimeSpan blockDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an event for a rate-limited subject.
    /// </summary>
    /// <param name="policyName">The rate-limit policy name.</param>
    /// <param name="resource">The resource identifier within the policy.</param>
    /// <param name="subject">The subject identifier within the policy.</param>
    /// <param name="cost">The event cost to add to the current count.</param>
    /// <param name="limit">The maximum accumulated count allowed before the subject is blocked.</param>
    /// <param name="windowDuration">How long events remain in the current rate-limit window.</param>
    /// <param name="windowBehavior">How the rate-limit window behaves when a new event is recorded.</param>
    /// <param name="blockDuration">How long a subject remains blocked after the rate limit has been exceeded.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The updated <see cref="RateLimitStatus"/>.</returns>
    public ValueTask<RateLimitStatus> Record(
        string policyName,
        string resource,
        string subject,
        ushort cost,
        int limit,
        TimeSpan windowDuration,
        RateLimitWindowBehavior windowBehavior,
        TimeSpan blockDuration,
        CancellationToken cancellationToken = default);
}
