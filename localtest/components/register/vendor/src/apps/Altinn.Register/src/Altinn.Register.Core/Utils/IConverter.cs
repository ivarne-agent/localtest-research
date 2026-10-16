using System.Diagnostics.CodeAnalysis;

namespace Altinn.Register.Core.Utils;

/// <summary>
/// Defines a mechanism for converting values from one type to another.
/// </summary>
/// <typeparam name="TSource">The type of the value to convert from.</typeparam>
/// <typeparam name="TResult">The type of the value to convert to.</typeparam>
public interface IConverter<in TSource, TResult>
{
    /// <summary>
    /// Tries to convert a <typeparamref name="TSource"/> to a <typeparamref name="TResult"/>.
    /// </summary>
    /// <param name="source">The source to convert from.</param>
    /// <param name="result">On return, contains the result of successfully converting <paramref name="source"/> or an undefined value on failure.</param>
    /// <returns><see langword="true"/> if <paramref name="source"/> was successfully converted; otherwise, <see langword="false"/>.</returns>
    bool TryConvert(TSource source, [MaybeNullWhen(false)] out TResult result);
}
