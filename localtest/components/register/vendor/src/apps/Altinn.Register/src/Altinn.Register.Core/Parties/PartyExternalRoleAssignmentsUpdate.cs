using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Core.Parties;

/// <summary>
/// Base class for updates to party external role assignments.
/// This is a union between a <see cref="Full"/> and a <see cref="Patch"/>.
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(Full), typeDiscriminator: "full")]
[JsonDerivedType(typeof(Patch), typeDiscriminator: "patch")]
public abstract record PartyExternalRoleAssignmentsUpdate
{
    /// <summary>
    /// Utility method to create a full update from a list of external role assignments, where each assignment is represented as a key-value pair of external role identifier and party UUID.
    /// </summary>
    /// <param name="assignments">The assignments.</param>
    /// <returns>A full update containing the provided assignments.</returns>
    public static PartyExternalRoleAssignmentsUpdate CreateFull(ReadOnlySpan<KeyValuePair<string, Guid>> assignments)
    {
        var builder = ImmutableArray.CreateBuilder<PartyExternalRoleAssignment>(assignments.Length);
        foreach (var kv in assignments)
        {
            builder.Add(new PartyExternalRoleAssignment
            {
                ToParty = new PartyExternalRoleAssignmentPartyRef.PartyUuid { Uuid = kv.Value },
                ExternalRoleIdentifier = kv.Key,
            });
        }

        return new Full
        {
            Assignments = builder.DrainToImmutableValueArray(),
        };
    }

    /// <summary>
    /// Utility method to create a full update from a list of external role assignments, where each assignment is represented as a key-value pair of external role identifier and party UUID.
    /// </summary>
    /// <param name="assignments">The assignments.</param>
    /// <returns>A full update containing the provided assignments.</returns>
    public static PartyExternalRoleAssignmentsUpdate CreateFull(IEnumerable<KeyValuePair<string, Guid>> assignments)
    {
        ImmutableArray<PartyExternalRoleAssignment>.Builder builder;
        if (assignments.TryGetNonEnumeratedCount(out var length))
        {
            builder = ImmutableArray.CreateBuilder<PartyExternalRoleAssignment>(length);
        }
        else
        {
            builder = ImmutableArray.CreateBuilder<PartyExternalRoleAssignment>();
        }

        foreach (var kv in assignments)
        {
            builder.Add(new PartyExternalRoleAssignment
            {
                ToParty = new PartyExternalRoleAssignmentPartyRef.PartyUuid { Uuid = kv.Value },
                ExternalRoleIdentifier = kv.Key,
            });
        }

        return new Full
        {
            Assignments = builder.DrainToImmutableValueArray(),
        };
    }

    // Prevents extending this record outside of this class.
    private PartyExternalRoleAssignmentsUpdate()
    {
    }

    /// <summary>
    /// Gets the update as a full update if this is a <see cref="Full"/> update.
    /// </summary>
    /// <param name="full">The full update if this is a <see cref="Full"/> update; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if this is a <see cref="Full"/> update; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue([NotNullWhen(true)] out Full? full)
    {
        if (this is Full f)
        {
            full = f;
            return true;
        }

        full = null;
        return false;
    }

    /// <summary>
    /// Gets the update as a delta update if this is a <see cref="Patch"/> update.
    /// </summary>
    /// <param name="delta">The delta update if this is a <see cref="Patch"/> update; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if this is a <see cref="Patch"/> update; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue([NotNullWhen(true)] out Patch? delta)
    {
        if (this is Patch d)
        {
            delta = d;
            return true;
        }

        delta = null;
        return false;
    }

    /// <summary>
    /// Represents a full update of party external role assignments, where the provided list of assignments
    /// is the complete and authoritative list of assignments for the party from the source.
    /// </summary>
    public sealed record Full
        : PartyExternalRoleAssignmentsUpdate
    {
        /// <summary>
        /// Gets an empty full update, where the list of assignments is empty, indicating that there are no role assignments for the party from the source.
        /// </summary>
        public static Full Empty { get; } = new() { Assignments = [] };

        /// <summary>
        /// Gets the complete list of party external role assignments for the party from the source.
        /// </summary>
        public required ImmutableValueArray<PartyExternalRoleAssignment> Assignments { get; init; }
    }

    /// <summary>
    /// Represents a delta update of party external role assignments, where the provided lists of assignments to add and remove
    /// are applied as changes to the existing assignments for the party from the source.
    /// </summary>
    /// <remarks>
    /// The absent assignments are removed before the present assignments are added,
    /// as such, if an assignment is present in both the absent and present lists,
    /// the assignment is first removed then re-added.
    /// </remarks>
    public sealed record Patch
        : PartyExternalRoleAssignmentsUpdate
    {
        /// <summary>
        /// Gets the list of external-role-identifiers for which all assignments will be removed.
        /// </summary>
        public required ImmutableValueArray<string> AbsentByIdentifier { get; init; }

        /// <summary>
        /// Gets the list of party external role assignments to remove,
        /// where each item identifies an assignment to remove by its external role identifier and the party
        /// referenced.
        /// </summary>
        public required ImmutableValueArray<PartyExternalRoleAssignment> Absent { get; init; }

        /// <summary>
        /// Gets the list of party external role assignments to add,
        /// where each item identifies an assignment to add by its external role identifier and the party
        /// referenced.
        /// </summary>
        public required ImmutableValueArray<PartyExternalRoleAssignment> Present { get; init; }
    }
}
