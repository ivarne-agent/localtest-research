using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Contracts.V1;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Operations;

namespace Altinn.Register.Operations.Validation;

/// <summary>
/// Input validators for <see cref="LookupV1PartyNamesRequest"/>
/// </summary>
internal readonly struct LookupV1PartyNamesRequestValidator
    : IValidator<PartyNamesLookup, IReadOnlyList<LookupV1PartyRequest>>
    , IValidator<IReadOnlyList<PartyLookup>?, IReadOnlyList<LookupV1PartyRequest>>
{
    /// <summary>
    /// The maximum number of items allowed in the request. A value of 0 or less means no limit.
    /// </summary>
    public int MaxItems { get; init; }

    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        PartyNamesLookup input,
        [NotNullWhen(true)] out IReadOnlyList<LookupV1PartyRequest>? validated)
    {
        return context.TryValidateChild(
            path: "/parties",
            input.Parties,
            this,
            out validated);
    }

    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        IReadOnlyList<PartyLookup>? input,
        [NotNullWhen(true)] out IReadOnlyList<LookupV1PartyRequest>? validated)
    {
        if (input is null or { Count: 0 })
        {
            validated = [];
            return true;
        }

        if (MaxItems > 0 && input.Count > MaxItems)
        {
            context.AddProblem(ValidationErrors.TooManyItems, detail: $"A maximum of {MaxItems} items are allowed.");
            validated = null;
            return false;
        }

        return ListValidator.ForEnumerable<PartyLookup, LookupV1PartyRequest, LookupV1PartyRequestValidator>(default)
            .TryValidate(ref context, input, out validated);
    }
}
