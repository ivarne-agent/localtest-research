using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Register.Contracts;

namespace Altinn.Register.PartyImport;

/// <summary>
/// A command for upserting external role assignments from a given party.
/// </summary>
public sealed record UpsertExternalRoleAssignmentsCommand
    : CommandBase
{
    /// <summary>
    /// Gets the party from which to upsert the external roles from.
    /// </summary>
    public required Guid FromPartyUuid { get; init; }

    /// <summary>
    /// Gets the party ID that the role assignments are from.
    /// </summary>
    /// <remarks>
    /// This was added later to make dealing with errors easier, as such, older messages
    /// does not contain this value and will default to 0. This is why this property is
    /// not marked as required as of now.
    /// </remarks>
    public uint FromPartyId { get; init; }

    /// <summary>
    /// Gets the source of the external role assignments.
    /// </summary>
    public required ExternalRoleSource Source { get; init; }

    /// <summary>
    /// Gets the new external role assignments.
    /// </summary>
    public required IReadOnlyList<Assignment> Assignments { get; init; }

    /// <summary>
    /// Gets the tracking information for the upsert.
    /// </summary>
    public required UpsertPartyTracking Tracking { get; init; }

    /// <summary>
    /// Represents an external role assignment.
    /// </summary>
    public sealed record Assignment
    {
        /// <summary>
        /// Gets the party which to assign the external role to.
        /// </summary>
        public required Guid ToPartyUuid { get; init; }

        /// <summary>
        /// Gets the role identifier.
        /// </summary>
        public required string Identifier { get; init; }
    }
}
