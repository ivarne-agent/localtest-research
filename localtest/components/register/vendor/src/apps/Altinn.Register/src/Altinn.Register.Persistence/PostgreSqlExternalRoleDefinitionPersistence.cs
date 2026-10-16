using System.Collections.Immutable;
using Altinn.Register.Contracts;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Persistence;

/// <summary>
/// Persistence service for external role definitions.
/// </summary>
internal sealed partial class PostgreSqlExternalRoleDefinitionPersistence
    : IExternalRoleDefinitionPersistence
{
    private readonly Cache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlExternalRoleDefinitionPersistence"/> class.
    /// </summary>
    public PostgreSqlExternalRoleDefinitionPersistence(Cache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc/>
    public ValueTask<ImmutableArray<ExternalRoleDefinition>> GetAllRoleDefinitions(CancellationToken cancellationToken = default)
        => _cache.GetAllRoleDefinitions(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<ExternalRoleDefinition?> TryGetRoleDefinition(ExternalRoleSource source, string identifier, CancellationToken cancellationToken = default)
        => _cache.TryGetRoleDefinition(source, identifier, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<ExternalRoleDefinition?> TryGetRoleDefinitionByRoleCode(string roleCode, CancellationToken cancellationToken = default)
        => _cache.TryGetRoleDefinitionByRoleCode(roleCode, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<IExternalRoleDefinitionLookup> GetRoleDefinitionLookup(CancellationToken cancellationToken = default)
        => _cache.GetRoleDefinitionLookup(cancellationToken);
}
