using Altinn.Register.Core.Location;

namespace Altinn.Register.Persistence.Location;

/// <summary>
/// Implementation for <see cref="ILocationLookupProvider"/> using PostgreSQL as the data source.
/// </summary>
internal sealed partial class PostgreSqlLocationLookupProvider
    : ILocationLookupProvider
{
    private readonly Cache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlLocationLookupProvider"/> class.
    /// </summary>
    public PostgreSqlLocationLookupProvider(Cache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc/>
    public ValueTask<ILocationLookup> GetLocationLookup(CancellationToken cancellationToken = default)
        => _cache.Get(cancellationToken);
}
