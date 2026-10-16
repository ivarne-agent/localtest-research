using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Core.Ccr;

/// <summary>
/// Represents a full or partial update to a Norwegian Central Coordinating Register for Legal Entities (CCR) (Enhetsregisteret ER) party,
/// including all relevant party information, and lists of co-changes to roles and connections.
/// </summary>
/// <remarks>Can be either a full update, where all fields are intended to replace existing data, or a partial update, where only specified fields should be updated.
/// Those fields that are implemented as <see cref="FieldValue{T}"/> can be used for both full and partial updates,
/// as they allow for distinguishing between "no change" and "set to null" scenarios. FieldValue.Unset indicate a partial update where the field should NOT be modified,
/// while FieldValue.Null indicates that the field should be explicitly set to null. FieldValue with a value indicates that the field should be updated to the specified value.
/// The RoleUpdates property can also be used for both full and partial updates, with the same logic for the fields within each <see cref="CcrRoleAssignment"/>.
/// </remarks>
[FieldValueRecord]
public sealed record CcrOrganizationUpdate
{
    /// <summary>
    /// Gets the organization identifier of the party, or <see langword="null"/> if the party is not an organization.
    /// </summary>
    public required OrganizationIdentifier OrganizationIdentifier { get; init; }

    /// <summary>
    /// Gets whether this is the first registration for the entity.
    /// </summary>
    public required bool IsFirstRegistration { get; init; }

    /// <summary>
    /// Gets the display-name of the party.
    /// </summary>
    public required FieldValue<string> DisplayName { get; init; }

    /// <summary>
    /// Gets whether the party is deleted.
    /// </summary>
    public required bool IsDeleted { get; init; }

    /// <summary>
    /// Gets when the party was deleted.
    /// </summary>
    public required DateOnly? DeletedAt { get; init; }

    /// <summary>
    /// Gets the organisation form.(Organisasjonsform)
    /// </summary>
    public FieldValue<string> UnitType { get; init; }

    /// <summary>
    /// Gets the status of the unit as extracted from the field value.
    /// </summary>
    public FieldValue<string> UnitStatus { get; init; }

    /// <summary>
    /// Gets the date last changed.
    /// </summary>
    public required DateOnly DatoSistEndret { get; init; }

    /// <summary>
    /// Gets the email address of the organization.
    /// </summary>
    public required FieldValue<string> EmailAddress { get; init; }

    /// <summary>
    /// Gets the internet address of the organization.
    /// </summary>
    public required FieldValue<string> InternetAddress { get; init; }

    /// <summary>
    /// Gets the telephone number of the organization.
    /// </summary>
    public required FieldValue<string> TelephoneNumber { get; init; }

    /// <summary>
    /// Gets the mobile number of the organization.
    /// </summary>
    public required FieldValue<string> MobileNumber { get; init; }

    /// <summary>
    /// Gets the fax number of the organization.
    /// </summary>
    public required FieldValue<string> FaxNumber { get; init; }

    /// <summary>
    /// Gets the mailing address of the organization.
    /// </summary>
    public required FieldValue<MailingAddressRecord> MailingAddress { get; init; }

    /// <summary>
    /// Gets the business address of the organization.
    /// </summary>
    public required FieldValue<MailingAddressRecord> BusinessAddress { get; init; }

    /// <summary>
    /// Gets the co-changes.
    /// </summary>
    public CcrRoleAssignmentsUpdate? RoleUpdates { get; init; }
}
