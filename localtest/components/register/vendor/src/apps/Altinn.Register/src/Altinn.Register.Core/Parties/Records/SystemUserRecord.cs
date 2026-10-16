using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Core.Parties.Records;

/// <summary>
/// A database record for a system user.
/// </summary>
[PolymorphicFieldValueRecord]
public sealed record SystemUserRecord
    : PartyRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SystemUserRecord"/> class.
    /// </summary>
    public SystemUserRecord()
        : base(PartyRecordType.SystemUser)
    {
    }

    /// <summary>
    /// Gets the type of the system user.
    /// </summary>
    public required FieldValue<SystemUserRecordType> SystemUserType { get; init; }
}
