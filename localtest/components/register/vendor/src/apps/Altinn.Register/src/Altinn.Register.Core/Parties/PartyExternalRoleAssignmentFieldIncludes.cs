using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Core.Parties;

/// <summary>
/// Fields to include when fetching <see cref="PartyExternalRoleAssignmentRecord"/>.
/// </summary>
[Flags]
public enum PartyExternalRoleAssignmentFieldIncludes
{
    /// <summary>
    /// No extra information (default).
    /// </summary>
    None = 0,

    /// <summary>
    /// The role source.
    /// </summary>
    RoleSource = 1 << 0,

    /// <summary>
    /// The role identifier.
    /// </summary>
    RoleIdentifier = 1 << 1,

    /// <summary>
    /// The UUID of the party the role is from.
    /// </summary>
    RoleFromParty = 1 << 2,

    /// <summary>
    /// The UUID of the party the role is to.
    /// </summary>
    RoleToParty = 1 << 3,

    /// <summary>
    /// All role fields.
    /// </summary>
    RoleAssignment = RoleSource | RoleIdentifier | RoleFromParty | RoleToParty,

    /// <summary>
    /// The role definition name.
    /// </summary>
    RoleDefinitionName = 1 << 4,

    /// <summary>
    /// The role definition description.
    /// </summary>
    RoleDefinitionDescription = 1 << 5,

    /// <summary>
    /// All role definition fields.
    /// </summary>
    RoleDefinition = RoleDefinitionName | RoleDefinitionDescription,
}
