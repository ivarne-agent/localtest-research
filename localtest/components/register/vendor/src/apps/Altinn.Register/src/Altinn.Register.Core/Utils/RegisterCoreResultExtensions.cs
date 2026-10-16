using Altinn.Authorization.ProblemDetails;

namespace Altinn.Register.Core.Utils;

/// <summary>
/// Result extensions.
/// </summary>
public static class RegisterCoreResultExtensions
{
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="result">The result.</param>
    extension<T>(Result<T> result)
        where T : notnull
    {
        /// <summary>
        /// Converts a <see cref="Result{T}"/> to a <see cref="Result{T2}"/> by applying the specified
        /// selector function to the value if the result is successful, or propagating the problem if
        /// the result is a problem.
        /// </summary>
        /// <typeparam name="T2">The type of the result after applying the selector function.</typeparam>
        /// <param name="selector">The function to apply to the result value if it is successful.</param>
        /// <returns>A <see cref="Result{T2}"/> representing the transformed result or the original problem.</returns>
        public Result<T2> Select<T2>(Func<T, T2> selector)
            where T2 : notnull
        {
            if (result.IsProblem)
            {
                return result.Problem;
            }

            return selector(result.Value);
        }
    }
}
