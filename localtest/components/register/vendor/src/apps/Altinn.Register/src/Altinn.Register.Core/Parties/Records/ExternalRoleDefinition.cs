using System.Diagnostics;
using Altinn.Register.Contracts;

namespace Altinn.Register.Core.Parties.Records;

/// <summary>
/// Represents an external role.
/// </summary>
[DebuggerDisplay("{Identifier,nq} ({Source,nq})")]
public sealed record ExternalRoleDefinition
{
    /// <summary>
    /// Gets the source of the external role.
    /// </summary>
    public required ExternalRoleSource Source { get; init; }

    /// <summary>
    /// Gets the identifier of the external role.
    /// </summary>
    /// <remarks>
    /// The identifier is unique within the source.
    /// </remarks>
    public required string Identifier { get; init; }

    /// <summary>
    /// Gets the name of the external role.
    /// </summary>
    public required TranslatedText Name { get; init; }

    /// <summary>
    /// Gets the description of the external role.
    /// </summary>
    public required TranslatedText Description { get; init; }

    /// <summary>
    /// Gets the legacy role-code of the external role, if it has one.
    /// </summary>
    public required string? Code { get; init; }

    /// <summary>
    /// Gets the role flags associated with the external role.
    /// </summary>
    public required RoleFlags Flags { get; init; }

    /// <summary>
    /// Gets the role flags associated with the external role.
    /// </summary>
    [Flags]
    public enum RoleFlags
        : ushort
    {
        /// <summary>
        /// No special role flags are set.
        /// </summary>
        None = 0,

        /// <summary>
        /// Indicates that the target of this role is considered the main unit of the organization.
        /// </summary>
        MainUnit = 1 << 0,
    }
}
