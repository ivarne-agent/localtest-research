using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;

namespace Altinn.Register.Core.Validation;

/// <summary>
/// Input validators for <see cref="PersonIdentifier"/>
/// </summary>
public readonly struct PersonIdentifierValidator
    : IValidator<string, PersonIdentifier>
    , IValidator<ReadOnlySpan<char>, PersonIdentifier>
{
    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        string? input,
        [NotNullWhen(true)] out PersonIdentifier? validated)
    {
        if (!PersonIdentifier.TryParse(input, provider: null, out validated))
        {
            context.AddProblem(ValidationErrors.InvalidPersonNumber);
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        ReadOnlySpan<char> input,
        [NotNullWhen(true)] out PersonIdentifier? validated)
    {
        if (!PersonIdentifier.TryParse(input, provider: null, out validated))
        {
            context.AddProblem(ValidationErrors.InvalidPersonNumber);
            return false;
        }

        return true;
    }
}
