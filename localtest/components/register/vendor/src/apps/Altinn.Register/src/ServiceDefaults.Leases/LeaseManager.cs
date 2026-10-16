using System.Diagnostics;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Authorization.ServiceDefaults.Leases;

/// <summary>
/// Transient manager for leases.
/// </summary>
public sealed class LeaseManager
{
    private static readonly ObjectFactory<OwnedLease> _factory
        = ActivatorUtilities.CreateFactory<OwnedLease>([typeof(ILeaseProvider), typeof(LeaseTicket), typeof(StackTrace), typeof(CancellationToken)]);

    private readonly ILeaseProvider _provider;
    private readonly IServiceProvider _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeaseManager"/> class.
    /// </summary>
    public LeaseManager(ILeaseProvider provider, IServiceProvider services)
    {
        Guard.IsNotNull(provider);
        Guard.IsNotNull(services);

        _provider = provider;
        _services = services;
    }

    /// <inheritdoc cref="AcquireLease(string, TimeSpan?, CancellationToken)"/>
    public Task<Lease> AcquireLease(
        string leaseId,
        CancellationToken cancellationToken)
        => AcquireLease(leaseId, ifUnacquiredFor: null, cancellationToken);

    /// <summary>
    /// Gets a auto-renewing lease for the specified lease id.
    /// </summary>
    /// <remarks>
    /// If <paramref name="cancellationToken"/> is cancelled, the lease will be released.
    /// </remarks>
    /// <param name="leaseId">The lease id.</param>
    /// <param name="ifUnacquiredFor">A filter that can be used to reject leases based on whether the lease has been unacquired for a certain amount of time.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="Lease"/>, if the lease was available, otherwise <see langword="null"/>.</returns>
    public async Task<Lease> AcquireLease(
        string leaseId,
        TimeSpan? ifUnacquiredFor = null,
        CancellationToken cancellationToken = default)
    {
        StackTrace? source = null;

#if DEBUG
        source = new StackTrace();
#endif

        LeaseTicket? ticket = null;

        try
        {
            var result = await _provider.TryAcquireLease(leaseId, OwnedLease.LeaseRenewalInterval, ifUnacquiredFor, cancellationToken);

            if (!result.IsLeaseAcquired)
            {
                return new Lease(leaseId, lease: null, expires: result.Expires, lastAcquiredAt: result.LastAcquiredAt, lastReleasedAt: result.LastReleasedAt);
            }

            ticket = result.Lease;
            var lease = _factory(_services, [_provider, ticket, source, cancellationToken]);

            ticket = null;
            return new Lease(lease.LeaseId, lease, expires: result.Expires, lastAcquiredAt: result.LastAcquiredAt, lastReleasedAt: result.LastReleasedAt);
        }
        finally
        {
            if (ticket is not null)
            {
                await _provider.ReleaseLease(ticket, cancellationToken: CancellationToken.None);
            }
        }
    }
}
