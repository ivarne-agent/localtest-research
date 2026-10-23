namespace Localtest.Emulated;

/// <summary>
/// pdf3 in minimal mode, on its own host (<c>pdf.{public host}</c>): every <c>POST /pdf</c> answers the same
/// one-page PDF ("Minimal PDF without content", <c>minimal.pdf</c>, embedded), which is all app-lib's client needs (a
/// success and bytes to store). <c>GET /</c> and <c>GET /pdf</c> answer a short text page naming the mode,
/// <c>/health/ready</c> and <c>/health/live</c> 200; any other path 404, as pdf3.
/// </summary>
public static class PdfEmulation
{
    public const string Service = "pdf3";

    /// <summary>The embedded <c>Pdf/minimal.pdf</c>.</summary>
    public const string Resource = "Localtest.Emulation.minimal.pdf";

    private static readonly Lazy<byte[]> Pdf = new(() =>
    {
        using var stream = typeof(PdfEmulation).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The embedded {Resource} is missing.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    });

    public static byte[] Bytes => Pdf.Value;

    /// <summary>Answers a request on the PDF host.</summary>
    public static async Task Handle(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var path = request.Path.Value ?? "/";
        if (HttpMethods.IsPost(request.Method) && path is "/pdf" or "/pdf/")
        {
            // The request (the page to print, the cookies) is not read: every PDF is the same page.
            response.ContentType = "application/pdf";
            response.ContentLength = Bytes.Length;
            await response.Body.WriteAsync(Bytes, context.RequestAborted);
            return;
        }

        if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) && path is "/health/ready" or "/health/live")
        {
            response.ContentType = "text/plain; charset=utf-8";
            await response.WriteAsync("ok\n", context.RequestAborted);
            return;
        }

        if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) && path is "/" or "/pdf" or "/pdf/")
        {
            response.ContentType = "text/plain; charset=utf-8";
            await response.WriteAsync(
                "Minimal mode: the PDF generator is emulated by the localtest container. POST /pdf answers the same one-page PDF "
                + "(\"Minimal PDF without content\") for every request; run the default stack for pdf3's real PDFs.\n",
                context.RequestAborted);
            return;
        }

        response.StatusCode = StatusCodes.Status404NotFound;
    }
}
