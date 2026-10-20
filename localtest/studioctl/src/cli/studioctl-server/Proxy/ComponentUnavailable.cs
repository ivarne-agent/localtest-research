using System.Net;
using System.Text;
using System.Text.Json;
using Altinn.Studio.EnvTopology;
using Microsoft.Net.Http.Headers;

namespace Altinn.Studio.StudioctlServer.Proxy;

/// <summary>
/// The front door's 503 for a route whose component cannot serve the request: its optional group is off, or it is
/// on and did not answer. The body is RFC 9457 problem details with the extension members <c>component</c> and
/// <c>group</c>, or the same content as a small HTML page when the request prefers <c>text/html</c> (a browser).
/// </summary>
internal static class ComponentUnavailable
{
    public const string NotRunningTitle = "Component not running";
    public const string NotAnsweringTitle = "Component not answering";

    // The components that are not containers of the environment, so `env logs` cannot show them.
    private const string AppComponent = "app";
    private const string FrontendDevServerComponent = "frontendDevServer";

    private const string UpCommand = "studioctl env up -r localtest-v2";

    /// <summary>The problem a disabled route answers: the component's group is off.</summary>
    public static Problem NotRunning(BoundTopologyRoute route, string path)
    {
        var group = GroupOf(route);
        var command = group is null ? UpCommand : $"{UpCommand} --with {group}";
        var detail = group is null
            ? $"The component {route.Component} is not running. Start the environment with: {command}"
            : $"The component {route.Component} belongs to the optional component group {group}, which is not running. "
                + $"Start it with: {command} (--with everything starts every optional group).";
        return new Problem(NotRunningTitle, detail, command, path, route.Component, group);
    }

    /// <summary>The problem an enabled route answers when the forwarder could not reach its component.</summary>
    public static Problem NotAnswering(BoundTopologyRoute route, string path, Uri destination)
    {
        var group = GroupOf(route);
        var (detail, command) = route.Component switch
        {
            AppComponent => (
                $"The app at {destination} did not answer. Is studioctl run still running for it?",
                "studioctl run"
            ),
            FrontendDevServerComponent => (
                $"The frontend dev server at {destination} did not answer. Start it, or run the app without --dev-frontend.",
                "studioctl run --dev-frontend"
            ),
            _ => (
                $"The component {route.Component} at {destination} did not answer. "
                    + $"See its log with: studioctl env logs -r localtest-v2 -c {route.Component}, "
                    + "and the state of every component with: studioctl env status -r localtest-v2",
                $"studioctl env logs -r localtest-v2 -c {route.Component}\nstudioctl env status -r localtest-v2"
            ),
        };
        return new Problem(NotAnsweringTitle, detail, command, path, route.Component, group);
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
    internal static bool PrefersHtml(HttpRequest request)
    {
        if (!MediaTypeHeaderValue.TryParseList(request.Headers.Accept, out var accept))
            return false;

        double html = 0;
        double json = 0;
        foreach (var value in accept)
        {
            var quality = value.Quality ?? 1;
            var type = value.MediaType.Value ?? string.Empty;
            if (IsAny(type, "text/html", "text/*", "application/xhtml+xml"))
                html = Math.Max(html, quality);
            else if (
                IsAny(type, "application/json", "application/problem+json", "application/*", "*/*")
                || type.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
            )
                json = Math.Max(json, quality);
        }
        return html > json;
    }

    private static bool IsAny(string type, params string[] candidates) =>
        candidates.Any(candidate => string.Equals(type, candidate, StringComparison.OrdinalIgnoreCase));

    private static string? GroupOf(BoundTopologyRoute route) =>
        route.Metadata.FirstOrDefault(entry => entry.Key == BoundTopologyMetadataKeys.Group)?.Value
            is { Length: > 0 } group
            ? group
            : null;

    private static byte[] Json(Problem problem)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("type", "https://www.rfc-editor.org/rfc/rfc9110#section-15.6.4");
            json.WriteString("title", problem.Title);
            json.WriteNumber("status", StatusCodes.Status503ServiceUnavailable);
            json.WriteString("detail", problem.Detail);
            json.WriteString("instance", problem.Instance);
            json.WriteString("component", problem.Component);
            if (problem.Group is null)
                json.WriteNull("group");
            else
                json.WriteString("group", problem.Group);
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
        html.Append(
            "<style>body{font-family:system-ui,sans-serif;max-width:46rem;margin:3rem auto;padding:0 1rem;"
                + "line-height:1.5;color:#1e2b3c;background:#fff}h1{font-size:1.5rem}"
                + "pre{background:#f1f3f5;padding:.75rem 1rem;border-radius:4px;overflow-x:auto}"
                + "dt{font-weight:600}dd{margin:0 0 .5rem}</style>\n</head>\n<body>\n"
        );
        html.Append("<h1>").Append(WebUtility.HtmlEncode(problem.Title)).Append("</h1>\n");
        html.Append("<p>").Append(WebUtility.HtmlEncode(problem.Detail)).Append("</p>\n");
        html.Append("<pre>").Append(WebUtility.HtmlEncode(problem.Command)).Append("</pre>\n");
        html.Append("<dl>\n<dt>Component</dt><dd>").Append(WebUtility.HtmlEncode(problem.Component)).Append("</dd>\n");
        if (problem.Group is not null)
            html.Append("<dt>Group</dt><dd>").Append(WebUtility.HtmlEncode(problem.Group)).Append("</dd>\n");
        html.Append("<dt>Path</dt><dd>").Append(WebUtility.HtmlEncode(problem.Instance)).Append("</dd>\n</dl>\n");
        html.Append("<p>503 Service Unavailable, from the localtest-v2 front door (studioctl-server).</p>\n");
        html.Append("</body>\n</html>\n");
        return html.ToString();
    }

    /// <param name="Title">The problem's title.</param>
    /// <param name="Detail">What is wrong and the command that changes it.</param>
    /// <param name="Command">The command alone, for the HTML page's <c>pre</c>.</param>
    /// <param name="Instance">The request path.</param>
    /// <param name="Component">The route's component.</param>
    /// <param name="Group">The component's optional group; null for an always-on component.</param>
    internal sealed record Problem(
        string Title,
        string Detail,
        string Command,
        string Instance,
        string Component,
        string? Group
    );
}
