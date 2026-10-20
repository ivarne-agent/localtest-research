using Microsoft.Net.Http.Headers;
using Yarp.ReverseProxy.Forwarder;

namespace Altinn.Studio.StudioctlServer.Proxy;

/// <summary>
/// What the localtest ingress always did for the platform behind it: the request keeps the browser-facing Host
/// (the components build their public links and validate issuers from it), and <c>Secure</c> is stripped from
/// every cookie, because the production components set it and the local origin is plain http. A route with a
/// <see cref="PathRewrite"/> is forwarded under its replacement path.
/// </summary>
internal sealed class PlatformTransformer(PathRewrite? rewrite = null) : HttpTransformer
{
    public override async ValueTask TransformRequestAsync(
        HttpContext httpContext,
        HttpRequestMessage proxyRequest,
        string destinationPrefix,
        CancellationToken cancellationToken
    )
    {
        await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);
        proxyRequest.Headers.Host = httpContext.Request.Host.Value;

        if (rewrite is { } pathRewrite)
        {
            proxyRequest.RequestUri = RequestUtilities.MakeDestinationAddress(
                destinationPrefix,
                pathRewrite.Apply(httpContext.Request.Path),
                httpContext.Request.QueryString
            );
        }
    }

    public override async ValueTask<bool> TransformResponseAsync(
        HttpContext httpContext,
        HttpResponseMessage? proxyResponse,
        CancellationToken cancellationToken
    )
    {
        var proceed = await base.TransformResponseAsync(httpContext, proxyResponse, cancellationToken);
        if (!proceed || proxyResponse is null)
            return proceed;

        var headers = httpContext.Response.Headers;
        if (headers.TryGetValue(HeaderNames.SetCookie, out var cookies) && cookies.Count > 0)
        {
            headers[HeaderNames.SetCookie] = new Microsoft.Extensions.Primitives.StringValues([
                .. cookies.Select(StripSecure),
            ]);
        }

        return true;
    }

    private static string? StripSecure(string? cookie)
    {
        if (cookie is null)
            return null;

        var parts = cookie
            .Split(';')
            .Select(static part => part.Trim())
            .Where(static part => !part.Equals("Secure", StringComparison.OrdinalIgnoreCase));
        return string.Join("; ", parts);
    }
}
