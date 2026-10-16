using System.Diagnostics.CodeAnalysis;

namespace Altinn.Register.Contracts.ExternalRoles;

/// <summary>
/// Represents a reference to an external role.
/// </summary>
public sealed record ExternalRoleReference
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalRoleReference"/> class.
    /// </summary>
    /// <param name="source">The source of the external role.</param>
    /// <param name="identifier">The identifier of the external role.</param>
    [SetsRequiredMembers]
    public ExternalRoleReference(ExternalRoleSource source, string identifier)
    {
        Source = source;
        Identifier = identifier;
    }

    /// <summary>
    /// Gets the source of the external role.
    /// </summary>
    public required ExternalRoleSource Source { get; init; }

    /// <summary>
    /// Gets the identifier of the external role.
    /// </summary>
    public required string Identifier { get; init; }
}
