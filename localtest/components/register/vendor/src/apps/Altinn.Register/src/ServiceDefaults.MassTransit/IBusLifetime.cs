using MassTransit;

namespace Altinn.Authorization.ServiceDefaults.MassTransit;

/// <summary>
/// Represents the lifetime of a masstransit bus.
/// </summary>
public interface IBusLifetime
{
    /// <summary>
    /// Waits for the bus to be ready.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>Information about the bus.</returns>
    Task<BusReady> WaitForBus(CancellationToken cancellationToken = default);
}
