using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Net.Http.Headers;

namespace Localtest.Proxy;

/// <summary>
/// The front door's 503 for a route whose service cannot serve the request: it has no address (no
/// <c>services__{name}__http__0</c>, so it is not part of the running stack), or it has one and did not answer. The
/// body is RFC 9457 problem details with the extension members <c>component</c> and <c>group</c>, or the same content
/// as a small HTML page when the request prefers <c>text/html</c> (a browser). Ported from studioctl-server's
/// <c>Proxy/ComponentUnavailable.cs</c>, which task 27 deletes; the hints name both runtimes.
/// </summary>
public static class ComponentUnavailable
{
    public const string NotRunningTitle = "Component not running";
    public const string NotAnsweringTitle = "Component not answering";

    private const string UpCommand = "studioctl env up -r localtest-v2";

    /// <summary>The problem a platform route answers when its service has no address.</summary>
    public static Problem NotRunning(string service, string path)
    {
        var group = PlatformRoutes.GroupOf(service);
        string detail;
        string command;
        if (group is null)
        {
            command = UpCommand + "\n" + "builder.AddAltinnLocaltest()";
            detail = $"The component {service} is not running: the localtest app has no address for it (services__{service}__http__0 is not set). "
                + $"It is part of the default stack. Start the environment with: {UpCommand}, or in an Aspire AppHost with builder.AddAltinnLocaltest().";
        }
        else if (group == PlatformRoutes.MonitoringGroup)
        {
            command = $"{UpCommand} --with {group}";
            detail = $"The component {service} belongs to the optional component group {group}, which is not running: the localtest app has no address for it (services__{service}__http__0 is not set). "
                + $"Start it with: {UpCommand} --with {group} (--with everything starts every optional group). An Aspire AppHost does not run this group: the Aspire dashboard shows the stack's and the apps' traces, metrics and logs.";
        }
        else
        {
            var typed = ".With" + string.Concat(group.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..])) + "()";
            command = $"{UpCommand} --with {group}\n{typed} or .WithGroup(\"{group}\")";
            detail = $"The component {service} belongs to the optional component group {group}, which is not running: the localtest app has no address for it (services__{service}__http__0 is not set). "
                + $"Start it with: {UpCommand} --with {group} (--with everything starts every optional group), or in an Aspire AppHost with {typed} or .WithGroup(\"{group}\") on AddAltinnLocaltest().";
        }

        return new Problem(NotRunningTitle, detail, command, path, service, group);
    }

    /// <summary>The problem a route answers when the forwarder could not reach its destination; <paramref name="error"/> is kept.</summary>
    public static Problem NotAnswering(Route route, string path, string destination, string error)
    {
        var (detail, command, group) = route.Kind switch
        {
            RouteKind.App => (
                $"The app {route.Name} at {destination} did not answer ({error}). Is it still running? studioctl run, or the Aspire AppHost, registers it again when it starts.",
                "studioctl run",
                (string?)null),
            RouteKind.FrontendDevServer => (
                $"The frontend dev server at {destination} did not answer ({error}). Start it, or remove its route (DELETE localtest/api/routes/frontend-dev-server) to serve the built app-frontend.",
                "studioctl run --dev-frontend",
                null),
            _ => (
                $"The component {route.Name} at {destination} did not answer ({error}). "
                    + $"See its log with: studioctl env logs -r localtest-v2 -c {route.Name}, and the state of every component with: studioctl env status -r localtest-v2 (in Aspire: the dashboard's console logs).",
                $"studioctl env logs -r localtest-v2 -c {route.Name}\nstudioctl env status -r localtest-v2",
                PlatformRoutes.GroupOf(route.Name)),
        };
        return new Problem(NotAnsweringTitle, detail, command, path, route.Name, group);
    }

    public static Task Write(HttpContext context, Problem problem)
    {
        var response = context.Response;
        response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        response.Headers.CacheControl = "no-store";
        if (PrefersHtml(context.Request))
        {
            response.ContentType = "text/html; charset=utf-8";
            return response.WriteAsync(Html(problem), context.RequestAborted);
        }

        response.ContentType = "application/problem+json";
        return response.Body.WriteAsync(Json(problem), context.RequestAborted).AsTask();
    }

    /// <summary>
    /// Whether the request ranks <c>text/html</c> above JSON. <c>*/*</c> counts for JSON only, so a client without an
    /// opinion (curl, an HTTP client) gets problem details, and a tie goes to JSON.
    /// </summary>
    public static bool PrefersHtml(HttpRequest request)
    {
        if (!MediaTypeHeaderValue.TryParseList(request.Headers.Accept, out var accept))
        {
            return false;
        }

        double html = 0;
        double json = 0;
        foreach (var value in accept)
        {
            var quality = value.Quality ?? 1;
            var type = value.MediaType.Value ?? string.Empty;
            if (IsAny(type, "text/html", "text/*", "application/xhtml+xml"))
            {
                html = Math.Max(html, quality);
            }
            else if (IsAny(type, "application/json", "application/problem+json", "application/*", "*/*") || type.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
            {
                json = Math.Max(json, quality);
            }
        }

        return html > json;
    }

    private static bool IsAny(string type, params string[] candidates)
        => candidates.Any(candidate => string.Equals(type, candidate, StringComparison.OrdinalIgnoreCase));

    private static byte[] Json(Problem problem)
    {
        using var buffer = new MemoryStream();
        // Relaxed escaping: the detail's quotes and apostrophes stay readable (\u0022 otherwise); the body is JSON, never HTML.
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteString("type", "https://www.rfc-editor.org/rfc/rfc9110#section-15.6.4");
            json.WriteString("title", problem.Title);
            json.WriteNumber("status", StatusCodes.Status503ServiceUnavailable);
            json.WriteString("detail", problem.Detail);
            json.WriteString("instance", problem.Instance);
            json.WriteString("component", problem.Component);
            if (problem.Group is null)
            {
                json.WriteNull("group");
            }
            else
            {
                json.WriteString("group", problem.Group);
            }

            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static string Html(Problem problem)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append("<title>").Append(WebUtility.HtmlEncode(problem.Title)).Append("</title>\n");
        html.Append("<style>body{font-family:system-ui,sans-serif;max-width:46rem;margin:3rem auto;padding:0 1rem;"
            + "line-height:1.5;color:#1e2b3c;background:#fff}h1{font-size:1.5rem}"
            + "pre{background:#f1f3f5;padding:.75rem 1rem;border-radius:4px;overflow-x:auto}"
            + "dt{font-weight:600}dd{margin:0 0 .5rem}</style>\n</head>\n<body>\n");
        html.Append("<h1>").Append(WebUtility.HtmlEncode(problem.Title)).Append("</h1>\n");
        html.Append("<p>").Append(WebUtility.HtmlEncode(problem.Detail)).Append("</p>\n");
        html.Append("<pre>").Append(WebUtility.HtmlEncode(problem.Command)).Append("</pre>\n");
        html.Append("<dl>\n<dt>Component</dt><dd>").Append(WebUtility.HtmlEncode(problem.Component)).Append("</dd>\n");
        if (problem.Group is not null)
        {
            html.Append("<dt>Group</dt><dd>").Append(WebUtility.HtmlEncode(problem.Group)).Append("</dd>\n");
        }

        html.Append("<dt>Path</dt><dd>").Append(WebUtility.HtmlEncode(problem.Instance)).Append("</dd>\n</dl>\n");
        html.Append("<p>503 Service Unavailable, from the localtest front door (the localtest app).</p>\n");
        html.Append("</body>\n</html>\n");
        return html.ToString();
    }

    /// <param name="Title">The problem's title.</param>
    /// <param name="Detail">What is wrong and what changes it.</param>
    /// <param name="Command">The commands alone, for the HTML page's <c>pre</c>.</param>
    /// <param name="Instance">The request path.</param>
    /// <param name="Component">The route's service, app (<c>{org}/{app}</c>) or <c>frontend-dev-server</c>.</param>
    /// <param name="Group">The service's optional group; null for the default stack, an app and the dev server.</param>
    public sealed record Problem(string Title, string Detail, string Command, string Instance, string Component, string? Group);
}
