using System.Globalization;
using System.Text;

namespace Altinn.Register.Core.Utils;

/// <summary>
/// Utility methods for comparing person names.
/// </summary>
public static class PersonNames
{
    /// <summary>
    /// Compares two last names using the same loose matching as the legacy person lookup flow.
    /// </summary>
    /// <param name="name1">The first last name.</param>
    /// <param name="name2">The second last name.</param>
    /// <returns><see langword="true"/> when the names are considered similar.</returns>
    public static bool IsLastNamesSimilar(string name1, string name2)
    {
        const int CompareLength = 4;

        name1 ??= string.Empty;
        name2 ??= string.Empty;

        name1 = name1.Trim().Length > CompareLength ? name1.Remove(CompareLength).Trim() : name1.Trim();
        name2 = name2.Trim().Length > CompareLength ? name2.Remove(CompareLength).Trim() : name2.Trim();

        name1 = RemoveDiacritics(name1);
        name2 = RemoveDiacritics(name2);

        return name1.Equals(name2, StringComparison.InvariantCultureIgnoreCase);
    }

    private static string RemoveDiacritics(string text)
    {
        string normalizedText;

        if (text.ToUpperInvariant().Contains('Å'))
        {
            StringBuilder firstPassBuilder = new();
            foreach (char ch in text)
            {
                if (ch == 'Å' || ch == 'å')
                {
                    firstPassBuilder.Append(ch.ToString().Normalize(NormalizationForm.FormC));
                }
                else
                {
                    firstPassBuilder.Append(ch.ToString().Normalize(NormalizationForm.FormD));
                }
            }

            normalizedText = firstPassBuilder.ToString();
        }
        else
        {
            normalizedText = text.Normalize(NormalizationForm.FormD);
        }

        StringBuilder secondPassBuilder = new();
        foreach (char ch in normalizedText)
        {
            UnicodeCategory unicode = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (unicode != UnicodeCategory.NonSpacingMark)
            {
                secondPassBuilder.Append(ch);
            }
        }

        return secondPassBuilder.ToString().Normalize(NormalizationForm.FormC);
    }
}
