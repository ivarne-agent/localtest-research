using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents a self-identified user party in Altinn Register.
/// </summary>
[PolymorphicFieldValueRecord]
public sealed record SelfIdentifiedUser()
    : Party(PartyType.SelfIdentifiedUser)
{
    /// <summary>
    /// Gets the type of the self-identified user.
    /// </summary>
    [JsonPropertyName("selfIdentifiedUserType")]
    public FieldValue<NonExhaustiveEnum<SelfIdentifiedUserType>> SelfIdentifiedUserType { get; init; }

    /// <summary>
    /// Gets the email of the self-identified user.
    /// </summary>
    /// <remarks>
    /// Only applicable for <see cref="SelfIdentifiedUserType.IdPortenEmail"/>.
    /// </remarks>
    [JsonPropertyName("email")]
    public FieldValue<string> Email { get; init; }
}
