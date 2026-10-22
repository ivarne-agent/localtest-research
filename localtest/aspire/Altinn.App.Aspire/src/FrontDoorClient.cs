using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Altinn.App.Aspire.Manifest;

namespace Altinn.App.Aspire;

/// <summary>
/// The front door's registration API (<c>localtest/localtest/README.md</c>, "Registering an app" and "The frontend dev
/// server"): <c>PUT localtest/api/apps/{org}/{app}</c> routes <c>/{org}/{app}/</c> to an app and provisions it from the
/// app's own API, <c>DELETE</c> deprovisions it and removes the route; <c>PUT localtest/api/routes/frontend-dev-server</c>
/// routes <c>app-frontend.local.altinn.cloud</c> to a Vite dev server. Every address is as the front door reaches it:
/// a port on the host through <c>host.docker.internal</c>.
/// </summary>
internal static class FrontDoorClient
{
    /// <summary>How long a call may take: a registration provisions the app on every service.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(100);

    private const int MaxBody = 2000;

    /// <summary>The path of an app's registration.</summary>
    public static string AppPath(AppId app) => $"localtest/api/apps/{app.Org}/{app.App}";

    /// <summary>The path of the frontend dev server's route.</summary>
    public const string DevServerPath = "localtest/api/routes/frontend-dev-server";

    /// <summary>A port on the host as the front door reaches it.</summary>
    public static string HostAddress(int port) =>
        $"{Uri.UriSchemeHttp}://{Topology.HostGatewayName}:{port.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Sends a PUT of <c>{ "url": address }</c>; returns the status and the body on one line.</summary>
    public static Task<FrontDoorAnswer> PutAsync(
        Uri platform,
        string path,
        string address,
        CancellationToken cancellationToken
    ) =>
        SendAsync(
            new HttpRequestMessage(HttpMethod.Put, new Uri(platform, path))
            {
                Content = JsonContent.Create(
                    new RegistrationRequest(address),
                    FrontDoorJsonContext.Default.RegistrationRequest
                ),
            },
            cancellationToken
        );

    /// <summary>Sends a DELETE of the path; returns the status and the body on one line.</summary>
    public static Task<FrontDoorAnswer> DeleteAsync(Uri platform, string path, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Delete, new Uri(platform, path)), cancellationToken);

    private static async Task<FrontDoorAnswer> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        using (request)
        using (var client = new HttpClient { Timeout = Timeout })
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))
                .ReplaceLineEndings(" ")
                .Trim();
            return new FrontDoorAnswer(
                request.Method,
                request.RequestUri!,
                (int)response.StatusCode,
                response.IsSuccessStatusCode,
                body.Length <= MaxBody ? body : body[..MaxBody] + "..."
            );
        }
    }
}

/// <summary>The front door's answer to one call.</summary>
/// <param name="Method">The call's method.</param>
/// <param name="Uri">The call's URL.</param>
/// <param name="StatusCode">The status.</param>
/// <param name="Succeeded">Whether the status is a success.</param>
/// <param name="Body">The body on one line, shortened.</param>
internal sealed record FrontDoorAnswer(HttpMethod Method, Uri Uri, int StatusCode, bool Succeeded, string Body)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"{Method} {Uri} answered {StatusCode.ToString(CultureInfo.InvariantCulture)}"
        + (Body.Length == 0 ? "" : ": " + Body);
}

/// <summary>The body of a registration.</summary>
/// <param name="Url">The address the front door forwards to.</param>
internal sealed record RegistrationRequest(string Url);

/// <summary>Source-generated JSON for the registration body.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RegistrationRequest))]
internal sealed partial class FrontDoorJsonContext : JsonSerializerContext;
