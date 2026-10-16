using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Npr;
using Altinn.Register.Core.Validation;

namespace Altinn.Register.Integrations.Npr.Feed;

/// <summary>
/// Validator for <see cref="UpdateItem"/>.
/// </summary>
public readonly struct UpdateItemValidator
    : IValidator<UpdateItem?, NprUpdate>
{
    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        UpdateItem? input,
        [NotNullWhen(true)] out NprUpdate? validated)
    {
        if (input is null)
        {
            context.AddProblem(StdValidationErrors.Required);
            validated = default;
            return false;
        }

        PersonIdentifier? personIdentifier = null;
        DateTimeOffset? updateTime = null;
        if (input.UpdateInfo is null)
        {
            context.AddChildProblem(StdValidationErrors.Required, "/hendelse");
        }
        else
        {
            if (input.UpdateInfo.PersonIdentifier is { } personIdentifierString)
            {
                context.TryValidateChild(
                    path: "/hendelse/folkeregisteridentifikator",
                    input: personIdentifierString,
                    validator: default(PersonIdentifierValidator),
                    out personIdentifier);
            }
            else
            {
                context.AddChildProblem(StdValidationErrors.Required, "/hendelse/folkeregisteridentifikator");
            }

            if (input.UpdateInfo.UpdateTime is { } updateTimeValue)
            {
                updateTime = updateTimeValue;
            }
            else
            {
                context.AddChildProblem(StdValidationErrors.Required, "/hendelse/ajourholdstidspunkt");
            }
        }

        if (context.HasErrors)
        {
            validated = default;
            return false;
        }

        Debug.Assert(personIdentifier is not null);
        Debug.Assert(updateTime is not null);
        validated = new NprUpdate
        {
            SequenceNumber = input.SequenceNumber,
            PersonIdentifier = personIdentifier,
            UpdateTime = updateTime.Value,
        };
        return true;
    }
}
