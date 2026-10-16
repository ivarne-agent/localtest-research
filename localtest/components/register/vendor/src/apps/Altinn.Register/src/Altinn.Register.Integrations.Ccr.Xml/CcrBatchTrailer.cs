using System.Globalization;
using System.Xml;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Integrations.Ccr.Xml;

/// <summary>
/// Represents the trailer record for a CCR batch, containing summary information about the batch contents.
/// </summary>
internal class CcrBatchTrailer
    : IXmlParsable<CcrBatchTrailer>
{
    /// <summary>
    /// Gets or sets the total number of parties included in the batch.
    /// </summary>
    public int AntallEnheter { get; set; }

    /// <summary>
    /// Gets or sets the total number of updates included in the batch.
    /// </summary>
    public required string Avsender { get; set; }

    /// <inheritdoc/>
    public static CcrBatchTrailer ParseNode(XmlReader reader)
    {
        reader.AssertStartElement("trai");
        reader.AssertEmptyElement();

        int parsedInt;
        int? antallEnheter = null;
        string? avsender = null;
        while (reader.MoveToNextAttribute())
        {
            switch (reader.LocalName)
            {
                case "avsender":
                    avsender = reader.Value;
                    break;

                case "antallEnheter":
                    if (!int.TryParse(reader.Value, style: NumberStyles.None, provider: CultureInfo.InvariantCulture, out parsedInt))
                    {
                        ThrowHelper.ThrowInvalidDataException($"Invalid 'antallEnheter' value in <trai> element: '{reader.Value}'. Expected a non-negative integer in invariant culture format.");
                    }

                    antallEnheter = parsedInt;
                    break;

                default:
                    // we ignore attributes we don't expect
                    // todo: log warning about unexpected attribute
                    break;
            }
        }

        reader.MoveToElement();
        reader.Read(); // Consume the <trai> element

        return new CcrBatchTrailer
        {
            Avsender = avsender ?? string.Empty,
            AntallEnheter = antallEnheter ?? ThrowHelper.ThrowInvalidDataException<int>("Missing required 'antallEnheter' attribute in <trai> element."),
        };
    }
}
