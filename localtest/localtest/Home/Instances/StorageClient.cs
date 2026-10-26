using System.Net.Http.Headers;

namespace Localtest.Home.Instances;

/// <summary>
/// Storage's instance API for the instances page, by service name (<c>http://storage/storage/api/v1/…</c>) with the
/// public <c>Host</c>, as the front door forwards a browser's (Storage builds its self and next links from it).
/// </summary>
public sealed class StorageClient(IHttpClientFactory httpClientFactory, LocaltestSettings localtest)
{
    public const string BasePath = "storage/api/v1/";

    /// <summary>Storage's answer: the status, the body as text, and the request that was sent (for the page).</summary>
    public sealed record Answer(int Status, string Body, string Request);

    public async Task<Answer> Get(string pathAndQuery, string? bearer, string? instanceOwnerIdentifier, CancellationToken cancellationToken)
    {
        using var request = Request(pathAndQuery, bearer, instanceOwnerIdentifier);
        using var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);
        return new Answer((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken), "GET /" + pathAndQuery);
    }

    /// <summary>A download: the response is the caller's to stream and dispose.</summary>
    public async Task<HttpResponseMessage> Send(string pathAndQuery, string? bearer, CancellationToken cancellationToken)
    {
        using var request = Request(pathAndQuery, bearer, null);
        var client = httpClientFactory.CreateClient();
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private HttpRequestMessage Request(string pathAndQuery, string? bearer, string? instanceOwnerIdentifier)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://storage/" + pathAndQuery);
        request.Headers.Host = localtest.PublicHost;
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (instanceOwnerIdentifier is not null)
        {
            request.Headers.Add(StorageQuery.InstanceOwnerIdentifierHeader, instanceOwnerIdentifier);
        }

        return request;
    }
}
