using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Localtest.Provisioning;

/// <summary>The few HTTP shapes the provisioner and <see cref="RunningAppSource"/> use.</summary>
internal static class PlatformHttp
{
    /// <summary>The body, or <c>null</c> on 404; any other failure throws.</summary>
    public static async Task<byte[]?> GetBytes(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await SendAsync(client, request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"GET {uri} returned {(int)response.StatusCode}: {Truncate(await response.Content.ReadAsStringAsync(cancellationToken))}"
            );
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public static async Task<string?> GetString(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        var bytes = await GetBytes(client, uri, cancellationToken);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Sends the request and throws with the status and the start of the body when it fails.</summary>
    public static async Task Send(
        HttpClient client,
        HttpMethod method,
        Uri uri,
        HttpContent? content,
        string? token,
        CancellationToken cancellationToken,
        bool notFoundIsSuccess = false
    )
    {
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(client, request, cancellationToken);
        if (notFoundIsSuccess && response.StatusCode == HttpStatusCode.NotFound)
            return;
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"{method} {uri} returned {(int)response.StatusCode}: {Truncate(body)}");
        }
    }

    /// <summary>
    /// <c>POST</c> to <paramref name="create"/>, and when that fails (the resource exists) <c>PUT</c> to
    /// <paramref name="replace"/>, as Designer's deploy does against Storage.
    /// </summary>
    public static async Task CreateOrReplace(
        HttpClient client,
        Uri create,
        Uri replace,
        Func<HttpContent> content,
        string token,
        CancellationToken cancellationToken
    )
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, create) { Content = content() })
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await SendAsync(client, request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return;
        }

        await Send(client, HttpMethod.Put, replace, content(), token, cancellationToken);
    }

    /// <summary>
    /// Sends, and names the request in a failure to send: a refused connection, a timeout, or a service name without an
    /// address (service discovery's <see cref="InvalidOperationException"/>, kept as the inner exception).
    /// </summary>
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException($"{request.Method} {request.RequestUri} failed: {ex.Message}", ex);
        }
    }

    /// <summary>A JSON body; no body at all (and no content type) for <c>null</c>.</summary>
    public static HttpContent Json(ReadOnlyMemory<byte>? body)
    {
        var content = new ReadOnlyMemoryContent(body ?? ReadOnlyMemory<byte>.Empty);
        if (body is not null)
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    public static string Truncate(string value) => value.Length <= 300 ? value : value[..300] + "...";
}
