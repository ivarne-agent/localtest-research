using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Core.ExternalRoles;

/// <summary>
/// Defines a lookup service for external role definitions, allowing retrieval of role definitions by source/identifier or role-code without asynchronous operations.
/// </summary>
public interface IExternalRoleDefinitionLookup
{
    /// <summary>
    /// Gets all available external role definitions. The returned collection is immutable and should reflect the state of the underlying data at the time of retrieval.
    /// </summary>
    public ImmutableArray<ExternalRoleDefinition> AllRoleDefinitions { get; }

    /// <summary>
    /// Tries to get the role definition for the specified source and identifier.
    /// </summary>
    /// <param name="source">The role definition source.</param>
    /// <param name="identifier">The role definition identifier.</param>
    /// <param name="roleDefinition">The role definition, if found.</param>
    /// <returns><see langword="true"/> if a role definition was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetRoleDefinition(ExternalRoleSource source, string identifier, [NotNullWhen(true)] out ExternalRoleDefinition? roleDefinition);

    /// <summary>
    /// Tries to get the role definition for the specified role-code.
    /// </summary>
    /// <param name="roleCode">The role definition role-code.</param>
    /// <param name="roleDefinition">The role definition, if found.</param>
    /// <returns><see langword="true"/> if a role definition was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetRoleDefinitionByRoleCode(string roleCode, [NotNullWhen(true)] out ExternalRoleDefinition? roleDefinition);
}
