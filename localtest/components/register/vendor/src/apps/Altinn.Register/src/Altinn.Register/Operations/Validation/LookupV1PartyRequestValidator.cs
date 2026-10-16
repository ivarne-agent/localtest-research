using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Contracts;
using Altinn.Register.Contracts.V1;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Operations;
using Altinn.Register.Core.Validation;

namespace Altinn.Register.Operations.Validation;

/// <summary>
/// Input validators for <see cref="LookupV1PartyRequest"/>
/// </summary>
internal readonly struct LookupV1PartyRequestValidator
    : IValidator<PartyLookup, LookupV1PartyRequest>
{
    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        PartyLookup input,
        [NotNullWhen(true)] out LookupV1PartyRequest validated)
    {
        const string ssnPath = "/ssn";
        const string orgNoPath = "/orgNo";

        switch (input?.Ssn, input?.OrgNo)
        {
            case (null, null):
                context.AddChildProblem(StdValidationErrors.Required, [ssnPath, orgNoPath], "Either ssn or orgNo is required.");
                break;

            case (not null, not null):
                context.AddChildProblem(ValidationErrors.MutuallyExclusive, [ssnPath, orgNoPath], "Only one of ssn and orgNo is allowed.");
                break;

            case (string ssn, null):
                if (context.TryValidateChild(path: ssnPath, ssn, default(PersonIdentifierValidator), out PersonIdentifier? personIdentifier))
                {
                    validated = new LookupV1PartyRequest(personIdentifier);
                    return true;
                }

                break;

            case (null, string orgNo):
                if (context.TryValidateChild(path: orgNoPath, orgNo, default(OrganizationIdentifierValidator), out OrganizationIdentifier? organizationIdentifier))
                {
                    validated = new LookupV1PartyRequest(organizationIdentifier);
                    return true;
                }

                break;
        }

        Debug.Assert(context.HasErrors);
        validated = default;
        return false;
    }
}
