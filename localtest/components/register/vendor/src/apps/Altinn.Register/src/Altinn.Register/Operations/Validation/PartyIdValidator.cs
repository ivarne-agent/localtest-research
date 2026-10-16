using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Core.Errors;

namespace Altinn.Register.Operations.Validation;

/// <summary>
/// Input validators for party id parameters.
/// </summary>
internal readonly struct PartyIdValidator
    : IValidator<int, uint>
    , IValidator<uint, uint>
{
    /// <inheritdoc/>
    public bool TryValidate(ref ValidationContext context, int input, [NotNullWhen(true)] out uint validated)
    {
        if (input <= 0)
        {
            context.AddProblem(ValidationErrors.InvalidPartyId);
            validated = default;
            return false;
        }

        validated = checked((uint)input);
        return true;
    }

    /// <inheritdoc/>
    public bool TryValidate(ref ValidationContext context, uint input, [NotNullWhen(true)] out uint validated)
    {
        if (input == 0)
        {
            context.AddProblem(ValidationErrors.InvalidPartyId);
            validated = default;
            return false;
        }

        validated = input;
        return true;
    }
}
