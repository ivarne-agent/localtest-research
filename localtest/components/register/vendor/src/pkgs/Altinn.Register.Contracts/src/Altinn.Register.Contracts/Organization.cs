using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents an organization party in Altinn Register.
/// </summary>
[PolymorphicFieldValueRecord]
public sealed record Organization()
    : Party(PartyType.Organization)
{
    /// <summary>
    /// Gets the organization identifier of the organization.
    /// </summary>
    [JsonPropertyName("organizationIdentifier")]
    public required OrganizationIdentifier OrganizationIdentifier { get; init; }

    /// <summary>
    /// Gets the source of the organization.
    /// </summary>
    [JsonPropertyName("source")]
    public FieldValue<NonExhaustiveEnum<OrganizationSource>> Source { get; init; }

    /// <summary>
    /// Gets the status of the organization.
    /// </summary>
    [JsonPropertyName("unitStatus")]
    public required FieldValue<string> UnitStatus { get; init; }

    /// <summary>
    /// Gets the type of the organization.
    /// </summary>
    [JsonPropertyName("unitType")]
    public required FieldValue<string> UnitType { get; init; }

    /// <summary>
    /// Gets the telephone number of the organization.
    /// </summary>
    [JsonPropertyName("telephoneNumber")]
    public required FieldValue<string> TelephoneNumber { get; init; }

    /// <summary>
    /// Gets the mobile number of the organization.
    /// </summary>
    [JsonPropertyName("mobileNumber")]
    public required FieldValue<string> MobileNumber { get; init; }

    /// <summary>
    /// Gets the fax number of the organization.
    /// </summary>
    [JsonPropertyName("faxNumber")]
    public required FieldValue<string> FaxNumber { get; init; }

    /// <summary>
    /// Gets the email address of the organization.
    /// </summary>
    [JsonPropertyName("emailAddress")]
    public required FieldValue<string> EmailAddress { get; init; }

    /// <summary>
    /// Gets the internet address of the organization.
    /// </summary>
    [JsonPropertyName("internetAddress")]
    public required FieldValue<string> InternetAddress { get; init; }

    /// <summary>
    /// Gets the mailing address of the organization.
    /// </summary>
    [JsonPropertyName("mailingAddress")]
    public required FieldValue<MailingAddress> MailingAddress { get; init; }

    /// <summary>
    /// Gets the business address of the organization.
    /// </summary>
    [JsonPropertyName("businessAddress")]
    public required FieldValue<MailingAddress> BusinessAddress { get; init; }
}
