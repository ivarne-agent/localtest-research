using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents a reference to an external role.
/// </summary>
public interface IExternalRoleRef
{
    /// <summary>
    /// Gets the source of the external role.
    /// </summary>
    public NonExhaustiveEnum<ExternalRoleSource> Source { get; }

    /// <summary>
    /// Gets the source-unique identifier of the external role.
    /// </summary>
    public string Identifier { get; }
}
