using System.Diagnostics;
using System.Text.Json.Serialization;
using Altinn.Register.Contracts;

namespace Altinn.Register.Core.Parties.Records;

/// <summary>
/// Represents an external role assignment event.
/// </summary>
[DebuggerDisplay("{Type,nq} {RoleIdentifier,nq} ({RoleSource,nq}) from {FromParty} to {ToParty}")]
public sealed record ExternalRoleAssignmentEvent
{
    /// <summary>
    /// Gets the version ID of the event.
    /// </summary>
    public required ulong VersionId { get; init; }

    /// <summary>
    /// Gets the type of the event.
    /// </summary>
    public required EventType Type { get; init; }

    /// <summary>
    /// Gets the role source of the event.
    /// </summary>
    public required ExternalRoleSource RoleSource { get; init; }

    /// <summary>
    /// Gets the role identifier of the event.
    /// </summary>
    public required string RoleIdentifier { get; init; }

    /// <summary>
    /// Gets the party the role is assigned to.
    /// </summary>
    public required Guid ToParty { get; init; }

    /// <summary>
    /// Gets the party the role is assigned from.
    /// </summary>
    public required Guid FromParty { get; init; }

    /// <summary>
    /// Role-assignment event type.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<EventType>))]
    public enum EventType
    {
        /// <summary>
        /// The role-assignment was added.
        /// </summary>
        Added,

        /// <summary>
        /// The role-assignment was removed.
        /// </summary>
        Removed,
    }
}
