using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Xml;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.PartyImport.Ccr;
using Altinn.Register.TestUtils.MassTransit;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

public abstract class CcrXmlUpdateTestBase
    : IntegrationTestBase
{
    protected abstract ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken);

    protected abstract ValueTask Verify(IUnitOfWork uow, CancellationToken cancellationToken);

    [StringSyntax(StringSyntaxAttribute.Xml)]
    protected abstract string XmlToApply { get; }

    [Fact]
    public async Task Run()
    {
        await Setup(async (uow, ct) =>
        {
            await Setup(uow, ct);
        });

        await ApplyXml(XmlToApply);

        await Check(async (uow, ct) =>
        {
            await Verify(uow, ct);
        });
    }

    [Fact]
    public async Task RunDense()
    {
        await Setup(async (uow, ct) =>
        {
            await Setup(uow, ct);
        });

        await ApplyXml(MakeDense(XmlToApply));

        await Check(async (uow, ct) =>
        {
            await Verify(uow, ct);
        });
    }

    [Fact]
    public async Task CallApi()
    {
        await Setup(async (uow, ct) =>
        {
            await Setup(uow, ct);
        });

        using var ms = new MemoryStream();
        using var writer = XmlWriter.Create(ms);
        writer.WriteStartDocument();
        writer.WriteStartElement("Envelope", "http://schemas.xmlsoap.org/soap/envelope/");

        writer.WriteStartElement("Header", "http://schemas.xmlsoap.org/soap/envelope/");
        writer.WriteEndElement(); // Header

        writer.WriteStartElement("Body", "http://schemas.xmlsoap.org/soap/envelope/");
        writer.WriteStartElement("SubmitERDataBasic", "http://www.altinn.no/services/Register/ER/2013/06");
        writer.WriteElementString("systemUserName", "http://www.altinn.no/services/Register/ER/2013/06", "test-user");
        writer.WriteElementString("systemPassword", "http://www.altinn.no/services/Register/ER/2013/06", "test-password");
        writer.WriteElementString("ERData", "http://www.altinn.no/services/Register/ER/2013/06", XmlToApply);
        writer.WriteEndElement(); // SubmitERDataBasic
        writer.WriteEndElement(); // Body
        writer.WriteEndElement(); // Envelope
        writer.WriteEndDocument();
        writer.Flush();

        using var content = new ByteArrayContent(ms.ToArray());
        content.Headers.ContentType = new("text/xml", charSet: "utf-8");
        content.Headers.Add("SOAPAction", "http://www.altinn.no/services/Register/ER/2013/06/IRegisterERExternalBasic/SubmitERDataBasic");

        using var request = new HttpRequestMessage(HttpMethod.Post, "enhets-registeret/api/v1/update.svc");
        request.Content = content;

        using var response = await HttpClient.SendAsync(request, CancellationToken);
        response.EnsureSuccessStatusCode();

        response.Headers.TryGetValues("X-Altinn-Register-Ccr-CommandId", out var values).ShouldBeTrue();
        var value = values.ShouldHaveSingleItem();
        Guid.TryParse(value, out var commandId).ShouldBeTrue();

        var conversation = await TestHarness.Conversation<ImportCcrXmlCommand>(cmd => cmd.CommandId == commandId, CancellationToken);
        var completedEvent = await conversation.Events.OfType<CcrXmlImportCompletedEvent>().FirstOrDefaultAsync(CancellationToken);
        completedEvent.ShouldNotBeNull();

        await Check(async (uow, ct) =>
        {
            await Verify(uow, ct);
        });
    }

    private async Task ApplyXml([StringSyntax(StringSyntaxAttribute.Xml)] string xml)
    {
        var ccrService = GetRequiredService<CcrService>();

        await ccrService.UpdateFromCcr(
            commandId: Guid.CreateVersion7(),
            input: new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(xml)),
            federate: true,
            cancellationToken: CancellationToken);
    }

    private static string MakeDense([StringSyntax(StringSyntaxAttribute.Xml)] string xml)
    {
        using var stringWriter = new StringWriter();
        using var writer = XmlWriter.Create(stringWriter, new XmlWriterSettings { CloseOutput = false, Indent = false, NewLineOnAttributes = false });
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { IgnoreWhitespace = true });

        writer.WriteNode(reader, defattr: false);
        writer.Flush();
        return stringWriter.ToString();
    }
}
