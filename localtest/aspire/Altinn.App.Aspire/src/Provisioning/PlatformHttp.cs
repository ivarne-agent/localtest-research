using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Altinn.Studio.Localtest.Provisioning;

/// <summary>The few HTTP shapes the provisioner and <see cref="RunningAppSource"/> use.</summary>
internal static class PlatformHttp
{
    /// <summary>The body, or <c>null</c> on 404; any other failure throws.</summary>
    public static async Task<byte[]?> GetBytes(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(uri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
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
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
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
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return;
        }

        await Send(client, HttpMethod.Put, replace, content(), token, cancellationToken);
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
