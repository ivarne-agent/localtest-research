using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using Altinn.Register.Integrations.Ccr.FileImport;
using Nerdbank.Streams;

namespace Altinn.Register.Persistence.Tests;

public static class ModuleInitializer
{
    private static int _initialized = 0;

    [ModuleInitializer]
    public static void Init()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            VerifierSettings.RegisterFileConverter<OrganizationUpdateDocument>(static (document, settings) =>
            {
                using var doc = document;

                var target = new Target(extension: "xml", data: ToXml(doc), name: document.OrganizationIdentifier.ToString());
                return new ConversionResult(document.OrganizationIdentifier.ToString(), [target]);
            });

            VerifierSettings.RegisterFileConverter<List<OrganizationUpdateDocument>>(static (documents, settings) =>
            {
                List<string> names = new(documents.Count);
                List<Target> targets = new(documents.Count);

                foreach (var document in documents.OrderBy(static d => d.OrganizationIdentifier.ToString(), StringComparer.Ordinal))
                {
                    using var doc = document;

                    var target = new Target(extension: "xml", data: ToXml(doc), name: document.OrganizationIdentifier.ToString());
                    targets.Add(target);
                    names.Add(document.OrganizationIdentifier.ToString());
                }

                return new ConversionResult(names, targets);
            });

            VerifierSettings.UseSplitModeForUniqueDirectory();
            UseProjectRelativeDirectory("Snapshots");
        }

        static string ToXml(OrganizationUpdateDocument doc)
        {
            using var textReader = new SequenceTextReader(doc.Document, Encoding.UTF8);

            var readerSettings = new XmlReaderSettings
            {
                IgnoreWhitespace = true,
            };

            var writerSettings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = false,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
            };

            var sb = new StringBuilder();
            {
                using var stringWriter = new StringWriter(sb) { NewLine = "\n" };
                using var xmlReader = XmlReader.Create(textReader, readerSettings);
                using var xmlWriter = XmlWriter.Create(stringWriter, writerSettings);

                xmlWriter.WriteNode(xmlReader, defattr: true);
                xmlWriter.Flush();

                stringWriter.WriteLine();
            }

            return sb.ToString();
        }
    }
}
