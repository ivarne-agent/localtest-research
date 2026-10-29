using Localtest.Provisioning;

namespace Localtest.Proxy;

/// <summary>
/// The front door's API for the runners, anonymous and local only. <c>localtest/api/apps</c>: one call registers an
/// app's route and provisions the app from its address; <c>DELETE</c> deprovisions it and removes the route; both are
/// idempotent. <c>localtest/api/routes</c>: the platform route table with each service's address, and the frontend dev
/// server's pushed route.
/// </summary>
public static class RoutesApi
{
    public static void Map(WebApplication app)
    {
        var apps = app.MapGroup("/localtest/api/apps");

        apps.MapGet("/", (AppRoutes routes) => routes.All.ToArray());

        apps.MapPut("/{org}/{app}", async Task<IResult> (string org, string app, RouteRequest request, AppRoutes routes, AppProvisioning provisioning, CancellationToken cancellationToken) =>
        {
            if (!AppRoutes.Segment().IsMatch(org) || !AppRoutes.Segment().IsMatch(app))
            {
                return Results.BadRequest("org and app must be lower-case letters, digits and hyphens");
            }

            if (PlatformRoutes.IsPlatformPath($"/{org}/") || org is "localtest" or "mockporten" or "app-frontend" or "Home" or "health")
            {
                return Results.BadRequest($"/{org}/ belongs to the platform or the localtest app; an app route there would shadow it");
            }

            if (Origin(request.Url) is not { } url)
            {
                return Results.BadRequest("url must be an absolute http(s) URL without a path, for example http://host.docker.internal:5005");
            }

            // The route first and kept whatever the provisioning does: the app answers through the front door either way.
            var registered = routes.Get(org, app) is { Url: var known, Registered: var at } && known == url ? at : DateTimeOffset.UtcNow;
            routes.Set(new AppRoute(org, app, url, registered, null));
            var status = await provisioning.Provision(org, app, url, cancellationToken);
            var route = new AppRoute(org, app, url, registered, status);
            routes.Set(route);
            return status.Result is not null
                ? Results.Json(route, LocaltestJsonContext.Default.AppRoute)
                : Results.Problem(
                    title: "Provisioning failed",
                    detail: $"{org}/{app} is routed to {url}, but provisioning it failed: {status.Error}. Register it again to retry.",
                    statusCode: StatusCodes.Status502BadGateway);
        });

        apps.MapDelete("/{org}/{app}", async Task<IResult> (string org, string app, AppRoutes routes, AppProvisioning provisioning, CancellationToken cancellationToken) =>
        {
            if (!AppRoutes.Segment().IsMatch(org) || !AppRoutes.Segment().IsMatch(app))
            {
                return Results.BadRequest("org and app must be lower-case letters, digits and hyphens");
            }

            // Idempotent: the test users are removed and the route is gone afterwards, whether or not there was one.
            var error = await provisioning.Deprovision(org, app, cancellationToken);
            routes.Remove(org, app);
            return error is null
                ? Results.NoContent()
                : Results.Problem(
                    title: "Deprovisioning failed",
                    detail: $"The route for {org}/{app} is removed, but removing its test users failed: {error}",
                    statusCode: StatusCodes.Status502BadGateway);
        });

        var routesApi = app.MapGroup("/localtest/api/routes");

        routesApi.MapGet("/", (HttpContext context, IConfiguration configuration, DevServerRoute devServer, LocaltestSettings localtest) => new RouteTable(
            [.. PlatformRoutes.Table(localtest).Select(r => new PlatformRouteEntry(r.Host, r.Prefix, r.Service, r.Rewrite, Address(context, configuration, r.Service)))],
            devServer.Current));

        routesApi.MapPut("/" + DevServerRoute.Name, (RouteRequest request, DevServerRoute devServer) =>
        {
            if (Origin(request.Url) is not { } url)
            {
                return Results.BadRequest("url must be an absolute http(s) URL without a path, for example http://host.docker.internal:8080");
            }

            var server = new DevServer(url, DateTimeOffset.UtcNow);
            devServer.Set(server);
            return Results.Json(server, LocaltestJsonContext.Default.DevServer);
        });

        routesApi.MapDelete("/" + DevServerRoute.Name, (DevServerRoute devServer) =>
        {
            devServer.Set(null);
            return Results.NoContent();
        });
    }

    /// <summary>A service's address: <c>emulated</c> when the front door emulates it (minimal mode), else its <c>services__</c> entry.</summary>
    private static string? Address(HttpContext context, IConfiguration configuration, string service)
        => context.RequestServices.GetService<IEmulatedServices>()?.Emulates(service) == true ? "emulated" : configuration[$"services:{service}:http:0"];

    /// <summary>The origin of an absolute http(s) URL without a path, query or fragment; null for anything else.</summary>
    private static string? Origin(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var url)
            && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
            && url.AbsolutePath == "/" && url.Query.Length == 0 && url.Fragment.Length == 0
                ? url.GetLeftPart(UriPartial.Authority)
                : null;
}

/// <summary><c>GET localtest/api/routes</c>.</summary>
public sealed record RouteTable(PlatformRouteEntry[] Platform, DevServer? FrontendDevServer);

/// <summary>Which services the front door emulates itself (minimal mode, registered by <c>Emulation/</c>).</summary>
public interface IEmulatedServices
{
    bool Emulates(string service);
}

/// <summary>A platform route and its service's address (<c>services__{service}__http__0</c>; <c>emulated</c>: the front door answers itself; null: not running).</summary>
public sealed record PlatformRouteEntry(string? Host, string? Prefix, string Service, string? Rewrite, string? Address);
