using Altinn.Authorization.ModelUtils.EnumUtils;
using Altinn.Register.Core.Parties;

namespace Altinn.Register.ModelBinding;

/// <summary>
/// Model binder for <see cref="PartyFieldIncludes"/>.
/// </summary>
public class PartyFieldIncludesModelBinder
    : FlagsEnumModelBinder<PartyFieldIncludes>
    , ISingleton<PartyFieldIncludesModelBinder>
{
    /// <summary>
    /// Gets the model for <see cref="PartyFieldIncludes"/>.
    /// </summary>
    public static FlagsEnumModel<PartyFieldIncludes> Model { get; }
        = FlagsEnumModel.Create<PartyFieldIncludes>();

    private PartyFieldIncludesModelBinder()
        : base(Model)
    {
    }

    /// <inheritdoc/>
    public static PartyFieldIncludesModelBinder Instance { get; } = new();

    /// <summary>
    /// Formats a <see cref="PartyFieldIncludes"/>.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The formatted <paramref name="value"/>.</returns>
    public static string? Format(PartyFieldIncludes value)
        => Model.Format(value);
}
