using Altinn.Register.Core.Parties;
using Altinn.Register.ModelBinding;

namespace Altinn.Register.ApiDescriptions;

/// <summary>
/// Schema filter for <see cref="PartyFieldIncludes"/>.
/// </summary>
public sealed class PartyFieldIncludesSchemaFilter
    : FlagsEnumSchemaFilter<PartyFieldIncludes>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PartyFieldIncludesSchemaFilter"/> class.
    /// </summary>
    public PartyFieldIncludesSchemaFilter()
        : base(PartyFieldIncludesModelBinder.Model)
    {
    }
}
