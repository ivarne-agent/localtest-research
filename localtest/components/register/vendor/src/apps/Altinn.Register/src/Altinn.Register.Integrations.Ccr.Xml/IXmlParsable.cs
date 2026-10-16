using System.Xml;

namespace Altinn.Register.Integrations.Ccr.Xml;

/// <summary>
/// Represents a type that can be parsed from an XML node using an <see cref="XmlReader"/>.
/// </summary>
/// <typeparam name="TSelf">The type that implements this interface.</typeparam>
internal interface IXmlParsable<TSelf>
    where TSelf : IXmlParsable<TSelf>
{
    /// <summary>
    /// Parses an XML node into a value.
    /// </summary>
    /// <param name="reader">The <see cref="XmlReader"/> to read from.</param>
    /// <returns>The parsed value.</returns>
    /// <remarks>
    /// When this method is called, the reader will be positioned on the start element of the node to parse.
    /// Implementations should read the entire element, including its end element, so that the reader
    /// is positioned on the next sibling element (or end of parent) when this method returns. This is validated
    /// in debug builds.
    /// </remarks>
    public static abstract TSelf ParseNode(XmlReader reader);
}
