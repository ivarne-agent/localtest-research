namespace Altinn.Register.Core.RateLimiting;

/// <summary>
/// Settings that define a rate-limiting policy.
/// </summary>
public sealed class RateLimitPolicySettings
{
    private TimeSpan? _blockDuration;

    /// <summary>
    /// Gets or sets a value indicating whether the policy settings have been explicitly configured.
    /// </summary>
    internal bool IsConfigured { get; set; }

    /// <summary>
    /// Gets or sets the maximum accumulated count allowed in the current window before the subject is blocked.
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    /// Gets or sets how long events remain in the current rate-limit window.
    /// </summary>
    public TimeSpan WindowDuration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets how the rate-limit window behaves when a new event is recorded.
    /// </summary>
    public RateLimitWindowBehavior WindowBehavior { get; set; } = RateLimitWindowBehavior.LeadingEdge;

    /// <summary>
    /// Gets or sets how long a subject remains blocked after the rate limit has been exceeded.
    /// Defaults to <see cref="WindowDuration"/> when not explicitly configured.
    /// </summary>
    public TimeSpan BlockDuration
    {
        get => _blockDuration ?? WindowDuration;
        set => _blockDuration = value;
    }

    /// <summary>
    /// Gets or sets how requests should be handled while the subject is already blocked.
    /// </summary>
    public BlockedRequestBehavior BlockedRequestBehavior { get; set; } = BlockedRequestBehavior.Ignore;
}
