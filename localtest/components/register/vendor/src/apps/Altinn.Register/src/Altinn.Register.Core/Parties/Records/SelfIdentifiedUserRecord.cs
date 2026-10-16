using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;

namespace Altinn.Register.Core.Parties.Records;

/// <summary>
/// A database record for a self-identified user.
/// </summary>
[PolymorphicFieldValueRecord]
public sealed record SelfIdentifiedUserRecord
    : PartyRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SelfIdentifiedUserRecord"/> class.
    /// </summary>
    public SelfIdentifiedUserRecord()
        : base(PartyRecordType.SelfIdentifiedUser)
    {
    }

    /// <summary>
    /// Gets the type of the self-identified user.
    /// </summary>
    [JsonPropertyName("selfIdentifiedUserType")]
    public required FieldValue<SelfIdentifiedUserType> SelfIdentifiedUserType { get; init; }

    /// <summary>
    /// Gets the email of the self-identified user.
    /// </summary>
    /// <remarks>
    /// Only applicable for <see cref="SelfIdentifiedUserType.IdPortenEmail"/>.
    /// </remarks>
    [JsonPropertyName("email")]
    public required FieldValue<string> Email { get; init; }

    /// <summary>
    /// Gets the external reference of the self-identified user.
    /// </summary>
    /// <remarks>
    /// Only applicable for <see cref="SelfIdentifiedUserType.Educational"/>.
    /// </remarks>
    [JsonPropertyName("extRef")]
    public required FieldValue<string> ExtRef { get; init; }
}
