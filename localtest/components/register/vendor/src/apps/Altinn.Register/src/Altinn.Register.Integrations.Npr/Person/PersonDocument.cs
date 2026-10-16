using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents the NPR person document payload.
/// </summary>
public sealed record PersonDocument
{
    /// <summary>
    /// Gets the person's identification number history.
    /// </summary>
    [JsonPropertyName("identifikasjonsnummer")]
    public ActiveElement<IdentificationNumberElement> IdentificationNumber { get; init; }

    /// <summary>
    /// Gets the person's status history.
    /// </summary>
    [JsonPropertyName("status")]
    public ActiveElement<PersonStatusElement> Status { get; init; }

    /// <summary>
    /// Gets the person's name history.
    /// </summary>
    [JsonPropertyName("navn")]
    public ActiveElement<NameElement> Name { get; init; }

    /// <summary>
    /// Gets the person's address protection history.
    /// </summary>
    [JsonPropertyName("adressebeskyttelse")]
    public ActiveElement<AddressProtectionElement> AddressProtection { get; init; }

    /// <summary>
    /// Gets the person's birth information history.
    /// </summary>
    [JsonPropertyName("foedsel")]
    public ActiveElement<BirthElement> Birth { get; init; }

    /// <summary>
    /// Gets the person's death information when registered.
    /// </summary>
    [JsonPropertyName("doedsfall")]
    public DeathElement? Death { get; init; }

    /// <summary>
    /// Gets the person's registered residential address history.
    /// </summary>
    [JsonPropertyName("bostedsadresse")]
    public ActiveElement<ResidentialAddressElement> RegisteredResidentialAddress { get; init; }

    /// <summary>
    /// Gets the person's current-stay address history.
    /// </summary>
    [JsonPropertyName("oppholdsadresse")]
    public ActiveElement<CurrentStayAddressElement> CurrentStayAddress { get; init; }

    /// <summary>
    /// Gets the person's mailing address history.
    /// </summary>
    [JsonPropertyName("postadresse")]
    public ActiveElement<MailingAddressElement> MailingAddress { get; init; }

    /// <summary>
    /// Gets the person's international mailing address history.
    /// </summary>
    [JsonPropertyName("postadresseIUtlandet")]
    public ActiveElement<InternationalMailingAddressElement> InternationalMailingAddress { get; init; }

    /// <summary>
    /// Gets the person's guardianship or future power-of-attorney history.
    /// </summary>
    [JsonPropertyName("vergemaalEllerFremtidsfullmakt")]
    public ActiveElementArray<GuardianshipOrPowerOfAttorneyElement> GuardianshipOrPowerOfAttorney { get; init; }
}
