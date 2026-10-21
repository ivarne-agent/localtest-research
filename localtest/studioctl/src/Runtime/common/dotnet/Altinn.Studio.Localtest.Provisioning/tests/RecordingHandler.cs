using System.Net;
using System.Text;

namespace Altinn.Studio.Localtest.Provisioning.Tests;

/// <summary>One request as the platform or the app saw it.</summary>
internal sealed record RecordedRequest(
    string Method,
    string Host,
    string PathAndQuery,
    string? Authorization,
    string? ContentType,
    string Body
)
{
    public override string ToString() => $"{Method} {PathAndQuery}";
}

/// <summary>Records every request and answers with <paramref name="respond"/>, 200 and an empty body by default.</summary>
internal sealed class RecordingHandler(Func<RecordedRequest, HttpResponseMessage?>? respond = null) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    public IEnumerable<string> Calls(string host) =>
        Requests.Where(r => r.Host == host).Select(static r => r.ToString());

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(
            request.Method.Method,
            request.RequestUri!.Host,
            request.RequestUri.PathAndQuery,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.MediaType,
            body
        );
        Requests.Add(recorded);
        return respond?.Invoke(recorded) ?? new HttpResponseMessage(HttpStatusCode.OK);
    }

    public static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8) };
}
