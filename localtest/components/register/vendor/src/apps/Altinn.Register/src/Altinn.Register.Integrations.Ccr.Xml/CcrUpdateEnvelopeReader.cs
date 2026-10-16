using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Xml;
using Nerdbank.Streams;

namespace Altinn.Register.Integrations.Ccr.Xml;

/// <summary>
/// Provides functionality to read and parse CCR update envelopes from SOAP XML messages.
/// </summary>
public sealed class CcrUpdateEnvelopeReader
{
    private const string NS_SOAP = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string NS_CCR = "http://www.altinn.no/services/Register/ER/2013/06";

    /// <summary>
    /// Reads a CCR update envelope from the provided byte sequence.
    /// </summary>
    /// <param name="data">The raw XML data as a byte sequence.</param>
    /// <returns>A result containing the CCR update envelope or an error.</returns>
    public static CcrUpdateEnvelope ReadEnvelope(ReadOnlySequence<byte> data)
    {
        using var xmlReader = XmlReader.Create(data.AsStream(), new XmlReaderSettings { IgnoreWhitespace = true });
        var reader = new CcrUpdateEnvelopeReader(xmlReader);

        return reader.ReadEnvelope();
    }

    private readonly XmlReader _reader;

    private CcrUpdateEnvelopeReader(XmlReader reader)
    {
        _reader = reader;
    }

    private CcrUpdateEnvelope ReadEnvelope()
    {
        ReadStartNode(NS_SOAP, "Envelope");
        SkipOptionalNode(NS_SOAP, "Header");
        ReadStartNode(NS_SOAP, "Body");
        ReadStartNode(NS_CCR, "SubmitERDataBasic");
        ReadStartNode(NS_CCR, "systemUserName");
        var userName = _reader.ReadContentAsString();
        _reader.ReadEndElement(); // systemUserName
        ReadStartNode(NS_CCR, "systemPassword");
        var password = _reader.ReadContentAsString();
        _reader.ReadEndElement(); // systemPassword
        ReadStartNode(NS_CCR, "ERData");
        var payload = _reader.ReadContentAsString();
        _reader.ReadEndElement(); // ERData
        _reader.ReadEndElement(); // SubmitERDataBasic
        _reader.ReadEndElement(); // Body
        _reader.ReadEndElement(); // Envelope

        return new CcrUpdateEnvelope
        {
            UserName = userName,
            Password = password,
            Payload = payload,
        };
    }

    private void ReadStartNode(string expectedNamespace, string expectedLocalName)
    {
        var type = _reader.MoveToContent();

        if (type != XmlNodeType.Element
            || _reader.LocalName != expectedLocalName
            || _reader.NamespaceURI != expectedNamespace)
        {
            Throw($"Expected element '{expectedLocalName}' in namespace '{expectedNamespace}', but found '{_reader.LocalName}' in namespace '{_reader.NamespaceURI}'.");
        }

        if (_reader.IsEmptyElement)
        {
            Throw($"Expected start element '{expectedLocalName}' in namespace '{expectedNamespace}', but found empty element.");
        }

        _reader.Read();
    }

    private void SkipOptionalNode(string expectedNamespace, string expectedLocalName)
    {
        var type = _reader.MoveToContent();

        if (type != XmlNodeType.Element
            || _reader.LocalName != expectedLocalName
            || _reader.NamespaceURI != expectedNamespace)
        {
            return;
        }

        _reader.Skip();
    }

    [DoesNotReturn]
    private static void Throw(string message)
    {
        throw new XmlException(message);
    }
}
