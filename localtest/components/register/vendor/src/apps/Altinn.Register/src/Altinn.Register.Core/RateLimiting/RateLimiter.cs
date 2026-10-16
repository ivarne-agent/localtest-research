using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Core.RateLimiting;

/// <summary>
/// Default implementation of <see cref="IRateLimiter"/>.
/// </summary>
internal sealed class RateLimiter
    : IRateLimiter
{
    private readonly IRateLimitProvider _provider;
    private readonly IOptionsMonitor<RateLimitPolicySettings> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimiter"/> class.
    /// </summary>
    /// <param name="provider">The underlying rate-limit provider.</param>
    /// <param name="options">The named rate-limit policy settings.</param>
    public RateLimiter(
        IRateLimitProvider provider,
        IOptionsMonitor<RateLimitPolicySettings> options)
    {
        _provider = provider;
        _options = options;
    }

    /// <inheritdoc/>
    public ValueTask<RateLimitStatus> GetStatus(
        string policyName,
        string resource,
        string subject,
        CancellationToken cancellationToken = default)
    {
        Guard.IsNotNullOrEmpty(policyName);
        Guard.IsNotNull(resource);
        Guard.IsNotNullOrEmpty(subject);

        var settings = _options.Get(policyName);
        return _provider.GetStatus(
            policyName,
            resource,
            subject,
            settings.BlockedRequestBehavior,
            settings.BlockDuration,
            cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<RateLimitStatus> Record(
        string policyName,
        string resource,
        string subject,
        ushort cost,
        CancellationToken cancellationToken = default)
    {
        Guard.IsNotNullOrEmpty(policyName);
        Guard.IsNotNull(resource);
        Guard.IsNotNullOrEmpty(subject);
        Guard.IsGreaterThan(cost, (ushort)0);

        var settings = _options.Get(policyName);
        return _provider.Record(
            policyName,
            resource,
            subject,
            cost,
            settings.Limit,
            settings.WindowDuration,
            settings.WindowBehavior,
            settings.BlockDuration,
            cancellationToken);
    }
}
