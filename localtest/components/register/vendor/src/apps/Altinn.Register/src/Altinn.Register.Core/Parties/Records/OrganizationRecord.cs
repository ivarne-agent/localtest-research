using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;

namespace Altinn.Register.Core.Parties.Records;

/// <summary>
/// A database record for an organization.
/// </summary>
[PolymorphicFieldValueRecord]
public sealed record OrganizationRecord
    : PartyRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrganizationRecord"/> class.
    /// </summary>
    public OrganizationRecord()
        : base(PartyRecordType.Organization)
    {
    }

    /// <summary>
    /// Gets the source of the organization.
    /// </summary>
    public required FieldValue<OrganizationSource> Source { get; init; }

    /// <summary>
    /// Gets the status of the organization.
    /// </summary>
    public required FieldValue<string> UnitStatus { get; init; }

    /// <summary>
    /// Gets the type of the organization.
    /// </summary>
    public required FieldValue<string> UnitType { get; init; }

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
    /// Gets the email address of the organization.
    /// </summary>
    public required FieldValue<string> EmailAddress { get; init; }

    /// <summary>
    /// Gets the internet address of the organization.
    /// </summary>
    public required FieldValue<string> InternetAddress { get; init; }

    /// <summary>
    /// Gets the mailing address of the organization.
    /// </summary>
    public required FieldValue<MailingAddressRecord> MailingAddress { get; init; }

    /// <summary>
    /// Gets the business address of the organization.
    /// </summary>
    public required FieldValue<MailingAddressRecord> BusinessAddress { get; init; }

    /// <summary>
    /// Gets the parent organization of the organization (if any).
    /// </summary>
    [JsonIgnore]
    public FieldValue<Guid> ParentOrganizationUuid { get; init; }
}
