using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents a system user party in Altinn Register.
/// </summary>
[PolymorphicFieldValueRecord]
public sealed record SystemUser()
    : Party(PartyType.SystemUser)
    , IOwnedParty
{
    /// <summary>
    /// Gets the owner of the system user.
    /// </summary>
    [JsonPropertyName("owner")]
    public required FieldValue<PartyRef> Owner { get; init; }

    /// <summary>
    /// Gets the type of the system user.
    /// </summary>
    [JsonPropertyName("systemUserType")]
    public required FieldValue<NonExhaustiveEnum<SystemUserType>> SystemUserType { get; init; }
}
