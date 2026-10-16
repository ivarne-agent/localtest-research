using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Sire;
using Altinn.Register.Core.Validation;

namespace Altinn.Register.Integrations.Sire.Feed;

/// <summary>
/// Validator for <see cref="UpdateItem"/>.
/// </summary>
public readonly struct UpdateItemValidator
    : IValidator<UpdateItem?, SireUpdate>
{
    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        UpdateItem? input,
        [NotNullWhen(true)] out SireUpdate? validated)
    {
        if (input is null)
        {
            context.AddProblem(StdValidationErrors.Required);
            validated = default;
            return false;
        }

        OrganizationIdentifier? organizationIdentifier = null;
        if (string.IsNullOrWhiteSpace(input.Identifier))
        {
            context.AddChildProblem(StdValidationErrors.Required, "/identifikator");
        }
        else
        {
            context.TryValidateChild(
                path: "/identifikator",
                input.Identifier,
                default(OrganizationIdentifierValidator),
                out organizationIdentifier);
        }

        DateTimeOffset? registeredAt = null;
        if (input.RegisteredAt is { } registeredAtValue)
        {
            registeredAt = registeredAtValue;
        }
        else
        {
            context.AddChildProblem(StdValidationErrors.Required, "/registreringstidspunkt");
        }

        NonExhaustiveEnum<SireUpdateType>? updateType = null;
        if (input.UpdateType is { } updateTypeValue)
        {
            updateType = updateTypeValue;
        }
        else
        {
            context.AddChildProblem(StdValidationErrors.Required, "/hendelsetype");
        }

        if (context.HasErrors)
        {
            validated = default;
            return false;
        }

        Debug.Assert(organizationIdentifier is not null);
        Debug.Assert(registeredAt is not null);
        Debug.Assert(updateType is not null);
        validated = new SireUpdate
        {
            SequenceNumber = input.SequenceNumber,
            OrganizationIdentifier = organizationIdentifier,
            RegisteredAt = registeredAt.Value,
            UpdateType = updateType.Value,
        };
        return true;
    }
}
