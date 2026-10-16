namespace Altinn.Register.Core.Location;

/// <summary>
/// Defines a provider for getting a cached <see cref="ILocationLookup"/> instance.
/// </summary>
public interface ILocationLookupProvider
{
    /// <summary>
    /// Gets a cached <see cref="ILocationLookup"/> instance.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A cached <see cref="ILocationLookup"/> instance.</returns>
    public ValueTask<ILocationLookup> GetLocationLookup(CancellationToken cancellationToken = default);
}
