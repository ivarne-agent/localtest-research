using Altinn.Studio.StudioctlServer.Proxy;
using Microsoft.AspNetCore.Http;

namespace Studioctl.Tests.Proxy;

public sealed class PlatformTransformerTests
{
    private const string RuntimeCookie =
        "AltinnStudioRuntime=jwt; domain=local.altinn.cloud; path=/; secure; samesite=lax; httponly";

    [Fact]
    public async Task APlatformComponentsCookiesPassUnchanged()
    {
        var cookies = await Forward(new PlatformTransformer(), RuntimeCookie, "AltinnPartyId=501337; path=/; Secure");

        Assert.Equal([RuntimeCookie, "AltinnPartyId=501337; path=/; Secure"], cookies);
    }

    [Fact]
    public async Task AnAppsCookiesLoseSecure()
    {
        var cookies = await Forward(new PlatformTransformer(stripSecure: true), RuntimeCookie, "XSRF-TOKEN=t; path=/");

        Assert.Equal(
            [
                "AltinnStudioRuntime=jwt; domain=local.altinn.cloud; path=/; samesite=lax; httponly",
                "XSRF-TOKEN=t; path=/",
            ],
            cookies
        );
    }

    private static async Task<string[]> Forward(PlatformTransformer transformer, params string[] setCookies)
    {
        var context = new DefaultHttpContext();
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        foreach (var setCookie in setCookies)
            response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);

        Assert.True(await transformer.TransformResponseAsync(context, response, CancellationToken.None));
        return [.. context.Response.Headers.SetCookie.Select(static c => c ?? string.Empty)];
    }
}
