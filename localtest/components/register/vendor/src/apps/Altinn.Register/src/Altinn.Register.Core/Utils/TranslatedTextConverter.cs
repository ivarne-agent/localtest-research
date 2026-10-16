using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Contracts;

namespace Altinn.Register.Core.Utils;

/// <summary>
/// A <see cref="IConverter{TSource, TResult}"/> for <see cref="TranslatedText"/>.
/// </summary>
public sealed class TranslatedTextConverter
    : IConverter<Dictionary<string, string>, TranslatedText>
{
    /// <summary>
    /// Gets the singleton instance of the TranslatedTextConverter.
    /// </summary>
    /// <remarks>Use this property to access a shared, thread-safe instance of the TranslatedTextConverter.
    /// This instance can be used wherever a TranslatedTextConverter is required, avoiding the need to create multiple
    /// instances.</remarks>
    private static TranslatedTextConverter Instance { get; } = new TranslatedTextConverter();

    /// <summary>
    /// Gets a converter that transforms a dictionary of database values into a TranslatedText instance.
    /// </summary>
    public static IConverter<Dictionary<string, string>, TranslatedText> FromDb => Instance;

    private TranslatedTextConverter()
    {
    }

    /// <inheritdoc/>
    bool IConverter<Dictionary<string, string>, TranslatedText>.TryConvert(Dictionary<string, string> source, [MaybeNullWhen(false)] out TranslatedText result)
    {
        if (source is null)
        {
            result = null;
            return false;
        }

        var builder = TranslatedText.CreateBuilder();
        foreach (var (key, value) in source)
        {
            if (!builder.TryAdd(LangCode.FromCode(key), value))
            {
                result = null;
                return false;
            }
        }

        return builder.TryToImmutable(out result);
    }
}
