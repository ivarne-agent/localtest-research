using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Core.Errors;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Provides validation for <see cref="ActiveElement{T}"/> of input models.
/// </summary>
internal static class ActiveElementValidator
{
    /// <summary>
    /// Creates an <see cref="IValidator{TIn, TOut}"/> for an <see cref="ActiveElement{T}"/> of input models.
    /// </summary>
    /// <typeparam name="TIn">The type of the input model.</typeparam>
    /// <typeparam name="TOut">The type of the output model.</typeparam>
    /// <typeparam name="TValidator">The type of the custom validator.</typeparam>
    /// <returns>An <see cref="IValidator{TIn, TOut}"/> for the input enumerable.</returns>
    public static RequiredActiveElementValidator<TIn, TOut, TValidator> Required<TIn, TOut, TValidator>(TValidator validator)
        where TOut : notnull
        where TIn : HistoricalElement
        where TValidator : IValidator<TIn, TOut>
    {
        return new RequiredActiveElementValidator<TIn, TOut, TValidator>(validator);
    }

    /// <summary>
    /// Creates an <see cref="IValidator{TIn, TOut}"/> for an <see cref="ActiveElement{T}"/> of input models.
    /// </summary>
    /// <typeparam name="TIn">The type of the input model.</typeparam>
    /// <typeparam name="TOut">The type of the output model.</typeparam>
    /// <typeparam name="TValidator">The type of the custom validator.</typeparam>
    /// <returns>An <see cref="IValidator{TIn, TOut}"/> for the input enumerable.</returns>
    public static OptionalActiveElementValidator<TIn, TOut, TValidator> Optional<TIn, TOut, TValidator>(TValidator validator)
        where TOut : notnull
        where TIn : HistoricalElement
        where TValidator : IValidator<TIn, Optional<TOut>>
    {
        return new OptionalActiveElementValidator<TIn, TOut, TValidator>(validator);
    }

    /// <summary>
    /// Creates an <see cref="IValidator{TIn, TOut}"/> for an <see cref="ActiveElementArray{T}"/> of input models.
    /// </summary>
    /// <typeparam name="TIn">The type of the input model.</typeparam>
    /// <typeparam name="TOut">The type of the output model.</typeparam>
    /// <typeparam name="TValidator">The type of the custom validator.</typeparam>
    /// <returns>An <see cref="IValidator{TIn, TOut}"/> for the input enumerable.</returns>
    public static ArrayOfOptionalActiveElementValidator<TIn, TOut, TValidator> ArrayOfOptional<TIn, TOut, TValidator>(TValidator validator)
        where TOut : notnull
        where TIn : HistoricalElement
        where TValidator : IValidator<TIn, Optional<TOut>>
    {
        return new ArrayOfOptionalActiveElementValidator<TIn, TOut, TValidator>(validator);
    }

    /// <summary>
    /// Validator for validating an <see cref="ActiveElement{T}"/>.
    /// </summary>
    /// <typeparam name="TIn">The type of the input element.</typeparam>
    /// <typeparam name="TOut">The type of the output element.</typeparam>
    /// <typeparam name="TValidator">The type of the validator.</typeparam>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal readonly struct RequiredActiveElementValidator<TIn, TOut, TValidator>
        : IValidator<ActiveElement<TIn>, TOut>
        where TOut : notnull
        where TIn : HistoricalElement
        where TValidator : IValidator<TIn, TOut>
    {
        private readonly TValidator _validator;

        /// <summary>
        /// Initializes a new instance of the <see cref="RequiredActiveElementValidator{TIn, TOut, TValidator}"/> struct.
        /// </summary>
        /// <param name="validator">The validator to use for validating the active element.</param>
        internal RequiredActiveElementValidator(TValidator validator)
        {
            _validator = validator;
        }

        /// <inheritdoc/>
        public bool TryValidate(
            ref ValidationContext context,
            ActiveElement<TIn> input,
            [NotNullWhen(true)] out TOut? validated)
        {
            // TODO: This should be a switch once we're on a version of C# that supports unions
            if (input.TryGetValue(out ActiveElement.Item<TIn> item))
            {
                var path = $"/{item.Index}";
                return context.TryValidateChild(path: path, item.Value, _validator, out validated);
            }

            if (input.TryGetValue(out ActiveElement.Multiple multiple))
            {
                var paths = multiple.Indices.Select(index => $"/{index}");
                context.AddChildProblem(ValidationErrors.MultipleActiveElements, path: paths);
                validated = default;
                return false;
            }

            Debug.Assert(!input.HasValue);
            context.AddProblem(StdValidationErrors.Required);
            validated = default;
            return false;
        }
    }

    /// <summary>
    /// Validator for validating an <see cref="ActiveElement{T}"/>.
    /// </summary>
    /// <typeparam name="TIn">The type of the input element.</typeparam>
    /// <typeparam name="TOut">The type of the output element.</typeparam>
    /// <typeparam name="TValidator">The type of the validator.</typeparam>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal readonly struct OptionalActiveElementValidator<TIn, TOut, TValidator>
        : IValidator<ActiveElement<TIn>, Optional<TOut>>
        where TOut : notnull
        where TIn : HistoricalElement
        where TValidator : IValidator<TIn, Optional<TOut>>
    {
        private readonly TValidator _validator;

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionalActiveElementValidator{TIn, TOut, TValidator}"/> struct.
        /// </summary>
        /// <param name="validator">The validator to use for validating the active element.</param>
        internal OptionalActiveElementValidator(TValidator validator)
        {
            _validator = validator;
        }

        /// <inheritdoc/>
        public bool TryValidate(
            ref ValidationContext context,
            ActiveElement<TIn> input,
            [NotNullWhen(true)] out Optional<TOut> validated)
        {
            // TODO: This should be a switch once we're on a version of C# that supports unions
            if (input.TryGetValue(out ActiveElement.Item<TIn> item))
            {
                var path = $"/{item.Index}";
                return context.TryValidateChild(path: path, item.Value, _validator, out validated);
            }

            if (input.TryGetValue(out ActiveElement.Multiple multiple))
            {
                var paths = multiple.Indices.Select(index => $"/{index}");
                context.AddChildProblem(ValidationErrors.MultipleActiveElements, path: paths);
                validated = default;
                return false;
            }

            Debug.Assert(!input.HasValue);
            validated = default;
            return true;
        }
    }

    /// <summary>
    /// Validator for validating an <see cref="ActiveElementArray{T}"/>.
    /// </summary>
    /// <typeparam name="TIn">The type of the input element.</typeparam>
    /// <typeparam name="TOut">The type of the output element.</typeparam>
    /// <typeparam name="TValidator">The type of the validator.</typeparam>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal readonly struct ArrayOfOptionalActiveElementValidator<TIn, TOut, TValidator>
        : IValidator<ActiveElementArray<TIn>, ImmutableArray<TOut>.Builder>
        where TOut : notnull
        where TIn : HistoricalElement
        where TValidator : IValidator<TIn, Optional<TOut>>
    {
        private readonly TValidator _validator;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArrayOfOptionalActiveElementValidator{TIn, TOut, TValidator}"/> struct.
        /// </summary>
        /// <param name="validator">The validator to use for validating the active element.</param>
        internal ArrayOfOptionalActiveElementValidator(TValidator validator)
        {
            _validator = validator;
        }

        /// <inheritdoc/>
        public bool TryValidate(
            ref ValidationContext context,
            ActiveElementArray<TIn> input,
            [NotNullWhen(true)] out ImmutableArray<TOut>.Builder? validated)
        {
            if (input.IsDefault)
            {
                context.AddProblem(StdValidationErrors.Required);
                validated = default;
                return false;
            }

            ImmutableArray<TOut>.Builder validatedList = ImmutableArray.CreateBuilder<TOut>(input.Length);

            foreach (var (index, item) in input)
            {
                if (context.TryValidateChild(path: $"/{index}", item, _validator, out Optional<TOut> validatedItem)
                    && !context.HasErrors
                    && validatedItem.HasValue)
                {
                    validatedList.Add(validatedItem.Value);
                }
            }

            if (context.HasErrors)
            {
                validated = default;
                return false;
            }

            validated = validatedList;
            return true;
        }
    }
}
