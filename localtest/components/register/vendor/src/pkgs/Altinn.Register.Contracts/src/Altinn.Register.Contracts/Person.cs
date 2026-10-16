using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents a person party in Altinn Register.
/// </summary>
[PolymorphicFieldValueRecord]
public sealed record Person()
    : Party(PartyType.Person)
{
    /// <summary>
    /// Gets the person identifier of the person.
    /// </summary>
    [JsonPropertyName("personIdentifier")]
    public required PersonIdentifier PersonIdentifier { get; init; }

    /// <summary>
    /// Gets the source of the person.
    /// </summary>
    [JsonPropertyName("source")]
    public FieldValue<NonExhaustiveEnum<PersonSource>> Source { get; init; }

    /// <summary>
    /// Gets the first name.
    /// </summary>
    [JsonPropertyName("firstName")]
    public required FieldValue<string> FirstName { get; init; }

    /// <summary>
    /// Gets the (optional) middle name.
    /// </summary>
    [JsonPropertyName("middleName")]
    public required FieldValue<string> MiddleName { get; init; }

    /// <summary>
    /// Gets the last name.
    /// </summary>
    [JsonPropertyName("lastName")]
    public required FieldValue<string> LastName { get; init; }

    /// <summary>
    /// Gets the short name.
    /// </summary>
    [JsonPropertyName("shortName")]
    public required FieldValue<string> ShortName { get; init; }

    /// <summary>
    /// Gets the (optional) <see cref="StreetAddress"/> of the person.
    /// </summary>
    [JsonPropertyName("address")]
    public required FieldValue<StreetAddress> Address { get; init; }

    /// <summary>
    /// Gets the (optional) <see cref="Contracts.MailingAddress"/> of the person.
    /// </summary>
    [JsonPropertyName("mailingAddress")]
    public required FieldValue<MailingAddress> MailingAddress { get; init; }

    /// <summary>
    /// Gets the date of birth of the person.
    /// </summary>
    [JsonPropertyName("dateOfBirth")]
    public required FieldValue<DateOnly> DateOfBirth { get; init; }

    /// <summary>
    /// Gets the (optional) date of death of the person.
    /// </summary>
    [JsonPropertyName("dateOfDeath")]
    public required FieldValue<DateOnly> DateOfDeath { get; init; }
}
