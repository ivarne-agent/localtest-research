using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Core.Ccr;

/// <summary>
/// Represents a role assignment with associated role code and optional personal identifier.
/// </summary>
public sealed record CcrRoleAssignment
{
    /// <summary>
    /// Default constructor, for serialization.
    /// </summary>
    private CcrRoleAssignment()
    {
    }

    /// <summary>
    /// True for Rolle and False for Knytning.
    /// Samendring can represent either a role change (Rolle) or a connection change (Knytning).
    /// </summary>
    public required RoleAssignmentType RoleAssignmentType { get; init; }

    /// <summary>
    /// Gets a value indicating whether the identifier represents a personal (user-specific) identity.
    /// </summary>
    [JsonIgnore]
    [MemberNotNullWhen(true, nameof(RolePersonalIdentifier))]
    public bool IsToPerson => RolePersonalIdentifier is not null;

    /// <summary>
    /// Gets a value indicating whether the role is associated with an organization.
    /// </summary>
    [JsonIgnore]
    [MemberNotNullWhen(true, nameof(RoleOrganizationNumber))]
    public bool IsToOrganization => RoleOrganizationNumber is not null;

    /// <summary>
    /// Gets or sets the code that identifies the user's role.
    /// </summary>
    public required string RoleCode { get; init; }

    /// <summary>
    /// Gets or sets the unique personal identifier associated with the role.
    /// </summary>
    public required PersonIdentifier? RolePersonalIdentifier { get; init; }

    /// <summary>
    /// Gets or sets the organization number associated with the role.
    /// </summary>
    public required OrganizationIdentifier? RoleOrganizationNumber { get; init; }

    #region BulkRoleAssignmentRemoval

    /// <summary>
    /// Creates a bulk role assignment removal for the specified set of roles ( implisit by code )
    /// </summary>
    /// <param name="code">The code of the roleset to remove. Cannot be null, empty, or consist only of white-space characters.</param>
    /// <returns>A <see cref="CcrRoleAssignment"/> representing the bulk role assignment removal for the specified role and
    /// subject.</returns>
    public static CcrRoleAssignment CreateBulkRoleAssignmentRemoval(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            ThrowHelper.ThrowArgumentException("Role code cannot be null or whitespace.", nameof(code));
        }

        return new CcrRoleAssignment
        {
            RoleAssignmentType = RoleAssignmentType.BulkRoleAssignmentRemoval,
            RoleCode = code,

            RolePersonalIdentifier = null,
            RoleOrganizationNumber = null,
            PersonName = null,
            MailingAddress = null,
        };
    }
    #endregion
    #region RoleAssignment

    /// <summary>
    /// Creates a new personal role assignment for the specified role code and personal identifier.
    /// </summary>
    /// <param name="code">The code representing the role to assign. Cannot be null or whitespace.</param>
    /// <param name="pid">The personal identifier associated with the role assignment. Cannot be null or whitespace.</param>
    /// <param name="personName">The given name, and optionally middle names</param>
    /// <param name="mailingAddress">A <see cref="MailingAddressRecord"/></param>
    /// <returns>A new instance of <see cref="CcrRoleAssignment"/> representing the personal role assignment.</returns>
    public static CcrRoleAssignment CreatePersonalRoleAssignment(
        string code,
        PersonIdentifier pid,
        PersonName? personName,
        MailingAddressRecord? mailingAddress)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            ThrowHelper.ThrowArgumentException("Role code cannot be null or whitespace.", nameof(code));
        }

        return new CcrRoleAssignment
        {
            RoleAssignmentType = RoleAssignmentType.RoleAssignment,
            RoleCode = code,
            RolePersonalIdentifier = pid,
            PersonName = personName,
            MailingAddress = mailingAddress,

            RoleOrganizationNumber = null,
        };
    }

    /// <summary>
    /// Gets the first name value associated with the field.
    /// </summary>
    public required PersonName? PersonName { get; init; }

    /// <summary>
    /// Gets the postal address associated with the entity.
    /// </summary>
    public required MailingAddressRecord? MailingAddress { get; init; }
    #endregion

    #region Connection

    /// <summary>
    /// Creates a new connection role assignment with the specified connection code and organization number.
    /// </summary>
    /// <param name="code">The unique code identifying the connection. Cannot be null, empty, or consist only of white-space characters.</param>
    /// <param name="organizationIdentifier">The organization number associated with the connection. Cannot be null, empty, or consist only of white-space
    /// characters.</param>
    /// <returns>A new instance of <see cref="CcrRoleAssignment"/> representing the connection role assignment.</returns>
    public static CcrRoleAssignment CreateConnection(string code, OrganizationIdentifier organizationIdentifier)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            ThrowHelper.ThrowArgumentException("Connection code cannot be null or whitespace.", nameof(code));
        }

        return new CcrRoleAssignment
        {
            RoleAssignmentType = RoleAssignmentType.Connection,
            RoleCode = code,
            RoleOrganizationNumber = organizationIdentifier,

            RolePersonalIdentifier = null,
            PersonName = null,
            MailingAddress = null,
        };
    }
    #endregion
}

/// <summary>
/// Specifies the type of role assignment operation to perform.
/// </summary>
/// <remarks>Use this enumeration to indicate whether the operation involves assigning a role, establishing a
/// connection, or performing a bulk removal of role assignments. This class is to be reworked into a Union in the future.</remarks>
[StringEnumConverter]
public enum RoleAssignmentType
{
    /// <summary>
    /// Add new roles
    /// </summary>
    [JsonStringEnumMemberName("role-assignment")]
    RoleAssignment,

    /// <summary>
    /// Add new connection for the org
    /// </summary>
    [JsonStringEnumMemberName("connection")]
    Connection,

    /// <summary>
    /// Removes multiple role assignments in a single batch. Such as the entire Board (styret)
    /// </summary>
    [JsonStringEnumMemberName("bulk-role-assignment-removal")]
    BulkRoleAssignmentRemoval,
}
