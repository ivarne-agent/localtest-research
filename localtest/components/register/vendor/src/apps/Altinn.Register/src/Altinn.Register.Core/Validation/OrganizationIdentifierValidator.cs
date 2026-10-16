using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;

namespace Altinn.Register.Core.Validation;

/// <summary>
/// Input validators for <see cref="OrganizationIdentifier"/>
/// </summary>
public readonly struct OrganizationIdentifierValidator
    : IValidator<string, OrganizationIdentifier>
    , IValidator<ReadOnlySpan<char>, OrganizationIdentifier>
{
    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        string input,
        [NotNullWhen(true)] out OrganizationIdentifier? validated)
    {
        if (!OrganizationIdentifier.TryParse(input, provider: null, out validated))
        {
            context.AddProblem(ValidationErrors.InvalidOrganizationNumber);
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        ReadOnlySpan<char> input,
        [NotNullWhen(true)] out OrganizationIdentifier? validated)
    {
        if (!OrganizationIdentifier.TryParse(input, provider: null, out validated))
        {
            context.AddProblem(ValidationErrors.InvalidOrganizationNumber);
            return false;
        }

        return true;
    }
}
