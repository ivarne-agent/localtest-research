using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents an enterprise user party in Altinn Register.
/// </summary>
[PolymorphicFieldValueRecord]
public sealed record EnterpriseUser()
    : Party(PartyType.EnterpriseUser)
    , IOwnedParty
{
    /// <summary>
    /// Gets the owner of the enterprise user.
    /// </summary>
    [JsonPropertyName("owner")]
    public required FieldValue<PartyRef> Owner { get; init; }
}
