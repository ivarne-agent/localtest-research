using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Core.Parties.Records;

/// <summary>
/// A database record for an enterprise user.
/// </summary>
[PolymorphicFieldValueRecord]
public sealed record EnterpriseUserRecord
    : PartyRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EnterpriseUserRecord"/> class.
    /// </summary>
    public EnterpriseUserRecord()
        : base(PartyRecordType.EnterpriseUser)
    {
    }
}
