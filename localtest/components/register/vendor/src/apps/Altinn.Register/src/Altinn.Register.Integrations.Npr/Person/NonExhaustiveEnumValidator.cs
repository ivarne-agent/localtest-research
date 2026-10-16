using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Core.Errors;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Validator for non-exhaustive enums, i.e. enums where the input might contain values not defined in the enum itself.
/// </summary>
/// <typeparam name="T">The type of the enum.</typeparam>
internal readonly struct NonExhaustiveEnumValidator<T>
    : IValidator<NonExhaustiveEnum<T>, T>
    where T : struct, Enum
{
    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        NonExhaustiveEnum<T> input,
        [NotNullWhen(true)] out T validated)
    {
        if (input.IsUnknown)
        {
            context.AddProblem(ValidationErrors.UnknownEnumValue, detail: $"The value '{input.UnknownValue}' is not a valid value for enum type '{typeof(T).Name}'.");
            validated = default;
            return false;
        }

        validated = input.Value;
        return true;
    }
}
