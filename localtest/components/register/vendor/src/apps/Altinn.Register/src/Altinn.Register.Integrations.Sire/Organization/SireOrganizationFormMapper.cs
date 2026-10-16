using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Core.Errors;

namespace Altinn.Register.Integrations.Sire.Organization;

/// <summary>
/// Maps SIRE <c>organisasjonsform</c> technical names to Skattelisten (SL) codes.
/// </summary>
/// <remarks>
/// SIRE returns the technical name (camelCase, e.g. "indreSelskap") in
/// <c>organisasjonsform</c>. We persist the SL-code (e.g. "KS") on
/// <c>SireOrganization.UnitType</c> to stay consistent with how organization forms from
/// other sources (CCR's "AS", "ENK", "NUF", …) are stored.
/// </remarks>
internal readonly struct SireOrganizationFormMapper
    : IValidator<string?, string>
{
    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        string? input,
        [NotNullWhen(true)] out string? validated)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            context.AddProblem(StdValidationErrors.Required);
            validated = null;
            return false;
        }

        validated = input switch
        {
            // Technical name (camelCase) -> SL-code.
            "ansvarligSelskapMedSolidariskAnsvar" => "ANS",
            "ansvarligSelskapMedDeltAnsvar" => "DA",
            "indreSelskap" => "IS",
            "kommandittselskap" => "KS",
            "norskkontrollertUtenlandskSelskap" => "NOKUS",
            "partrederi" => "PRE",
            "tingsrettsligSameie" => "SAM",
            "utenlandskAnsvarligSelskap" => "UTLANS",
            "virksomhetDrevetIFellesskap" => "VIFE",
            _ => null,
        };

        if (validated is null)
        {
            context.AddProblem(
                ValidationErrors.UnknownEnumValue,
                detail: $"Unknown SIRE organisasjonsform: '{input}'.");
            return false;
        }

        return true;
    }
}
