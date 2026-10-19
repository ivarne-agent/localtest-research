namespace Localtest.Proxy;

/// <summary>
/// The proxy's API, under <c>localtest/api/proxy/</c> (each feature of the localtest app gets its own
/// <c>localtest/api/{feature}/</c>): the app runner registers and removes app routes, and anyone can list them and the
/// platform upstreams. Anonymous, local only.
/// </summary>
public static class AppRoutesApi
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/localtest/api/proxy");

        api.MapGet("/apps", (AppRoutes routes) => routes.All.ToArray());

        api.MapPut("/apps/{org}/{app}", (string org, string app, AppRouteRequest request, AppRoutes routes) =>
        {
            if (!AppRoutes.Segment().IsMatch(org) || !AppRoutes.Segment().IsMatch(app))
            {
                return Results.BadRequest("org and app must be lower-case letters, digits and hyphens");
            }

            if (PlatformRoutes.IsPlatformPath($"/{org}/") || org == "localtest")
            {
                return Results.BadRequest($"/{org}/ belongs to the platform or the localtest app; an app route there would shadow it");
            }

            if (!Uri.TryCreate(request.Upstream, UriKind.Absolute, out var upstream)
                || (upstream.Scheme != Uri.UriSchemeHttp && upstream.Scheme != Uri.UriSchemeHttps)
                || upstream.AbsolutePath != "/")
            {
                return Results.BadRequest("upstream must be an absolute http(s) URL without a path, for example http://host.docker.internal:5005");
            }

            var route = new AppRoute(org, app, upstream.GetLeftPart(UriPartial.Authority), DateTimeOffset.UtcNow);
            routes.Set(route);
            return Results.Ok(route);
        });

        api.MapDelete("/apps/{org}/{app}", (string org, string app, AppRoutes routes)
            => routes.Remove(org, app) ? Results.NoContent() : Results.NotFound());

        api.MapGet("/upstreams", (PlatformRoutes platform) => platform.Upstreams.ToDictionary(u => u.Key, u => u.Value));
    }
}
