using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;

namespace Altinn.Register.Core.Parties.Records;

/// <summary>
/// A database record for a role assignment.
/// </summary>
public class PartyExternalRoleAssignmentRecord
{
    /// <summary>
    /// Gets the role source.
    /// </summary>
    public required FieldValue<ExternalRoleSource> Source { get; init; }

    /// <summary>
    /// Gets the role identifier (unique within the source).
    /// </summary>
    public required FieldValue<string> Identifier { get; init; }

    /// <summary>
    /// Gets the role name.
    /// </summary>
    public required FieldValue<TranslatedText> Name { get; init; }

    /// <summary>
    /// Gets the role description.
    /// </summary>
    public required FieldValue<TranslatedText> Description { get; init; }

    /// <summary>
    /// Gets the UUID of the party the role is assigned from.
    /// </summary>
    public required FieldValue<Guid> FromParty { get; init; }

    /// <summary>
    /// Gets the UUID of the party the role is assigned to.
    /// </summary>
    public required FieldValue<Guid> ToParty { get; init; }
}
