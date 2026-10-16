using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Register.Contracts.Parties;
using MassTransit;

namespace Altinn.Register.Contracts.ExternalRoles;

/// <summary>
/// An event that is published when an external role assignment is added.
/// </summary>
[MessageUrn("event:altinn-register:external-role-assignment-added")]
public sealed record ExternalRoleAssignmentAddedEvent
    : EventBase
{
    /// <summary>
    /// Gets the version ID of the event.
    /// </summary>
    public required ulong VersionId { get; init; }

    /// <summary>
    /// Gets the role that was assigned.
    /// </summary>
    public required ExternalRoleReference Role { get; init; }

    /// <summary>
    /// Gets the party that the role was assigned from.
    /// </summary>
    public required PartyReference From { get; init; }

    /// <summary>
    /// Gets the party that the role was assigned to.
    /// </summary>
    public required PartyReference To { get; init; }
}
