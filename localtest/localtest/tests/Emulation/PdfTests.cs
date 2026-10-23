using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Localtest.Emulated;

namespace Localtest.Tests.Emulation;

/// <summary>The static PDF: a well-formed PDF 1.4 with a correct cross-reference table, answered on the PDF host.</summary>
public sealed partial class PdfTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void TheFileIsAOnePagePdfWithACorrectCrossReferenceTable()
    {
        var bytes = PdfEmulation.Bytes;
        var text = Encoding.ASCII.GetString(bytes);

        Assert.StartsWith("%PDF-1.4\n", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
        Assert.Contains("(Minimal PDF without content) Tj", text, StringComparison.Ordinal);
        Assert.Contains("/MediaBox [0 0 595 842]", text, StringComparison.Ordinal);
        Assert.Contains("/Count 1", text, StringComparison.Ordinal);

        // startxref points at the table; each entry's offset at "n 0 obj".
        var startxref = int.Parse(StartXref().Match(text).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("xref\n0 6\n", text[startxref..], StringComparison.Ordinal);
        var entries = Entry().Matches(text[startxref..]).Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(5, entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            Assert.StartsWith($"{i + 1} 0 obj\n", text[entries[i]..], StringComparison.Ordinal);
        }

        // The content stream's /Length is its byte count.
        var stream = StreamBody().Match(text);
        Assert.Equal(int.Parse(stream.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), stream.Groups[2].Value.Length);
    }

    [Fact]
    public void TheEmbeddedFileIsTheCommittedOne()
        => Assert.Equal(
            File.ReadAllBytes(Path.Combine(TestDataTests.LocaltestFolder(), "localtest/Emulation/Pdf/minimal.pdf")),
            PdfEmulation.Bytes);

    [Fact]
    public async Task ThePdfHostAnswersThePdfForEveryPostAndIsPdf3Otherwise()
    {
        await using var frontDoor = new MinimalFrontDoor();
        using var client = frontDoor.Client();

        using var post = new HttpRequestMessage(HttpMethod.Post, "/pdf") { Content = MinimalFrontDoor.Json("""{"url":"http://local.altinn.cloud:8000/ttd/app/#/instance/1/2?pdf=1"}""") };
        post.Headers.Host = "pdf.local.altinn.cloud:8000";
        using var pdf = await client.SendAsync(post, Ct);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PdfEmulation.Bytes, await pdf.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal("pdf3", Assert.Single(pdf.Headers.GetValues("X-Localtest-Emulated")));

        Assert.Equal(HttpStatusCode.OK, await Status(client, "/health/ready"));
        Assert.Equal(HttpStatusCode.OK, await Status(client, "/"));
        // As pdf3: its health is /health/ready, not the front door's /health.
        Assert.Equal(HttpStatusCode.NotFound, await Status(client, "/health"));
    }

    private static async Task<HttpStatusCode> Status(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = "pdf.local.altinn.cloud:8000";
        using var response = await client.SendAsync(request, Ct);
        return response.StatusCode;
    }

    [GeneratedRegex(@"startxref\n(\d+)\n%%EOF")]
    private static partial Regex StartXref();

    [GeneratedRegex(@"(\d{10}) 00000 n \n")]
    private static partial Regex Entry();

    [GeneratedRegex(@"<< /Length (\d+) >>\nstream\n(.*?)\nendstream", RegexOptions.Singleline)]
    private static partial Regex StreamBody();
}
