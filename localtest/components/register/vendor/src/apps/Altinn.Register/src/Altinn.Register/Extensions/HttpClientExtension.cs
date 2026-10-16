namespace Altinn.Register.Extensions;

/// <summary>
/// This extension is created to make it easy to add a bearer token to a HttpRequests.
/// </summary>
public static class HttpClientExtension
{
    /// <summary>
    /// Extension that add authorization header to request
    /// </summary>
    /// <param name="httpClient">The HttpClient</param>
    /// <param name="authorizationToken">the authorization token (jwt)</param>
    /// <param name="requestUri">The request Uri</param>
    /// <param name="platformAccessToken">The platformAccess tokens</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>A HttpResponseMessage</returns>
    public static Task<HttpResponseMessage> GetAsync(this HttpClient httpClient, string authorizationToken, string requestUri, string? platformAccessToken = null, CancellationToken cancellationToken = default)
    {
        HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Add("Authorization", "Bearer " + authorizationToken);

        if (!string.IsNullOrEmpty(platformAccessToken))
        {
            request.Headers.Add("PlatformAccessToken", platformAccessToken);
        }

        return httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
    }
}
