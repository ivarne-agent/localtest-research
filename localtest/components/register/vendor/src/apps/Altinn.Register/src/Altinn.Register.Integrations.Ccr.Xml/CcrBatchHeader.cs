using System.Xml;

namespace Altinn.Register.Integrations.Ccr.Xml;

/// <summary>
/// Represents the header information for a CCR batch, including sender, receiver, date, and batch number details.
/// </summary>
internal class CcrBatchHeader
    : IXmlParsable<CcrBatchHeader>
{
    /// <summary>
    /// Gets or sets the sender of the message.
    /// </summary>
    public required string Avsender { get; set; }

    /// <summary>
    /// Gets or sets the date value as a string.
    /// </summary>
    public required string Dato { get; set; }

    /// <summary>
    /// Gets or sets the batch number.
    /// </summary>
    public required string Kjoerenr { get; set; }

    /// <summary>
    /// Gets or sets the recipient of the message.
    /// </summary>
    public required string Mottaker { get; set; }

    /// <summary>
    /// The type of the batch
    /// </summary>
    public required string Type { get; set; }

    /// <inheritdoc/>
    public static CcrBatchHeader ParseNode(XmlReader reader)
    {
        reader.AssertStartElement("head");
        reader.AssertEmptyElement();

        string? avsender = null, dato = null, kjoerenr = null, mottaker = null, type = null;
        while (reader.MoveToNextAttribute())
        {
            switch (reader.LocalName)
            {
                case "avsender":
                    avsender = reader.Value;
                    break;

                case "dato":
                    dato = reader.Value;
                    break;

                case "kjoerenr":
                    kjoerenr = reader.Value;
                    break;

                case "mottaker":
                    mottaker = reader.Value;
                    break;

                case "type":
                    type = reader.Value;
                    break;

                default:
                    // we ignore attributes we don't expect
                    // todo: log warning about unexpected attribute
                    break;
            }
        }

        reader.MoveToElement();
        reader.Read(); // Consume the <head> element

        return new CcrBatchHeader
        {
            Avsender = avsender ?? string.Empty,
            Dato = dato ?? string.Empty,
            Kjoerenr = kjoerenr ?? string.Empty,
            Mottaker = mottaker ?? string.Empty,
            Type = type ?? string.Empty
        };
    }
}
