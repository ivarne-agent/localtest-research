using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Altinn.Authorization.TestUtils;

namespace Altinn.Register.Contracts.Tests.Shouldly;

[ShouldlyMethods]
[DebuggerStepThrough]
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class ShouldlyComponentModelValidationExtensions
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ShouldBeValidComponentModel(this object value, string? customMessage = null)
    {
        List<ValidationResult> validationResults = [];
        ValidationContext ctx = new(value, null, null);
        if (!Validator.TryValidateObject(value, ctx, validationResults, true))
        {
            throw new ShouldAssertException(new ValidationErrorShouldlyMessage(validationResults, value, customMessage).ToString());
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IReadOnlyList<ValidationResult> ShouldNotBeValidComponentModel(this object value, string? customMessage = null)
    {
        List<ValidationResult> validationResults = [];
        ValidationContext ctx = new(value, null, null);
        if (Validator.TryValidateObject(value, ctx, validationResults, true))
        {
            throw new ShouldAssertException(new ActualShouldlyMessage(value, customMessage).ToString());
        }

        return validationResults;
    }

    private sealed class ValidationErrorShouldlyMessage
        : ActualShouldlyMessage
    {
        public IReadOnlyCollection<ValidationResult> Results { get; }

        public ValidationErrorShouldlyMessage(
            IReadOnlyCollection<ValidationResult> results,
            object actual,
            string? customMessage,
            [CallerMemberName] string shouldlyMethod = null!)
            : base(actual, customMessage, shouldlyMethod)
        {
            Results = results;
        }

        public override string ToString()
        {
            var context = ShouldlyAssertionContext;
            var codePart = context.CodePart;
            var actual = context.Actual;

            var actualString =
                $"""

                {actual}
                """;

            var validationErrorsString = new StringBuilder();
            foreach (var result in Results)
            {
                validationErrorsString.Append("     - ");
                validationErrorsString.Append(result.ErrorMessage);
                validationErrorsString.Append(" [");
                validationErrorsString.Append(string.Join(", ", result.MemberNames));
                validationErrorsString.AppendLine("]");
            }

            var message =
                $"""
                 {codePart}
                     {StringHelpers.PascalToSpaced(context.ShouldMethod)}
                     but was{actualString}
                     with validation errors:
                 {validationErrorsString}
                 """;

            if (ShouldlyAssertionContext.CustomMessage != null)
            {
                message += $"""


                        Additional Info:
                            {ShouldlyAssertionContext.CustomMessage}
                        """;
            }

            return message;
        }
    }
}
