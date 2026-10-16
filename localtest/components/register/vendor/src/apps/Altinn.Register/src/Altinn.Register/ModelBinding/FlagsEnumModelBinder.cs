using Altinn.Authorization.ModelUtils.EnumUtils;
using Altinn.Register.Model.Extensions;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Altinn.Register.ModelBinding;

/// <summary>
/// Base class for model binding a flags enum.
/// </summary>
/// <typeparam name="TEnum">The flags enum type.</typeparam>
public abstract class FlagsEnumModelBinder<TEnum>
    : ModelBinder<TEnum>
    where TEnum : struct, Enum
{
    private readonly FlagsEnumModel<TEnum> _model;

    /// <summary>
    /// Initializes the model binder.
    /// </summary>
    /// <param name="model">The enum model.</param>
    protected FlagsEnumModelBinder(FlagsEnumModel<TEnum> model)
    {
        _model = model;
    }

    /// <inheritdoc/>
    public override Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var values = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (values.Length == 0)
        {
            bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        var result = default(TEnum);

        bool hasErrors = false;
        foreach (var value in values)
        {
            foreach (var name in value.AsSpan().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                result = result.BitwiseOr(Parse(name, bindingContext, ref hasErrors));
            }
        }

        if (hasErrors)
        {
            bindingContext.Result = ModelBindingResult.Failed();
            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(result);
        return Task.CompletedTask;
    }

    private TEnum Parse(ReadOnlySpan<char> name, ModelBindingContext bindingContext, ref bool hasErrors)
    {
        if (!_model.TryParse(name, out var value))
        {
            hasErrors = true;
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, $"Invalid value: '{name}'");
            return default;
        }

        return value;
    }
}
