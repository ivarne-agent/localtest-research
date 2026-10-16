namespace Altinn.Register.Core.RateLimiting;

/// <summary>
/// High-level rate-limiting API with convenience overloads for policies that use a single resource.
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// The default resource identifier for policies that do not distinguish between multiple resources.
    /// </summary>
    public const string DefaultResource = "";

    /// <summary>
    /// Gets the current <see cref="RateLimitStatus"/> for a rate-limited subject.
    /// </summary>
    /// <remarks>
    /// This operation is not guaranteed to be a pure read: depending on policy configuration,
    /// calling it for an already-blocked subject may extend or otherwise modify the block state.
    /// Callers should not poll this method or assume it is side-effect free.
    /// </remarks>
    /// <param name="policyName">The rate-limit policy name.</param>
    /// <param name="resource">The resource identifier within the policy. Use <see cref="DefaultResource"/> when the policy has only one resource.</param>
    /// <param name="subject">The subject identifier within the policy.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The current <see cref="RateLimitStatus"/>.</returns>
    public ValueTask<RateLimitStatus> GetStatus(
        string policyName,
        string resource,
        string subject,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current <see cref="RateLimitStatus"/> for a rate-limited subject using <see cref="DefaultResource"/>.
    /// </summary>
    /// <remarks>
    /// This operation is not guaranteed to be a pure read: depending on policy configuration,
    /// calling it for an already-blocked subject may extend or otherwise modify the block state.
    /// Callers should not poll this method or assume it is side-effect free.
    /// </remarks>
    /// <param name="policyName">The rate-limit policy name.</param>
    /// <param name="subject">The subject identifier within the policy.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The current <see cref="RateLimitStatus"/>.</returns>
    public ValueTask<RateLimitStatus> GetStatus(
        string policyName,
        string subject,
        CancellationToken cancellationToken = default)
        => GetStatus(policyName, DefaultResource, subject, cancellationToken);

    /// <summary>
    /// Records an event for a rate-limited subject.
    /// </summary>
    /// <param name="policyName">The rate-limit policy name.</param>
    /// <param name="resource">The resource identifier within the policy. Use <see cref="DefaultResource"/> when the policy has only one resource.</param>
    /// <param name="subject">The subject identifier within the policy.</param>
    /// <param name="cost">The event cost to add to the current count.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The updated <see cref="RateLimitStatus"/>.</returns>
    public ValueTask<RateLimitStatus> Record(
        string policyName,
        string resource,
        string subject,
        ushort cost = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an event for a rate-limited subject using <see cref="DefaultResource"/>.
    /// </summary>
    /// <param name="policyName">The rate-limit policy name.</param>
    /// <param name="subject">The subject identifier within the policy.</param>
    /// <param name="cost">The event cost to add to the current count.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The updated <see cref="RateLimitStatus"/>.</returns>
    public ValueTask<RateLimitStatus> Record(
        string policyName,
        string subject,
        ushort cost = 1,
        CancellationToken cancellationToken = default)
        => Record(policyName, DefaultResource, subject, cost, cancellationToken);
}
