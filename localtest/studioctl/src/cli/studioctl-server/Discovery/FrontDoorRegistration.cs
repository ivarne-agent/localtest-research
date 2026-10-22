using System.Net.Http.Json;
using System.Text.Json;
using Altinn.Studio.StudioctlServer.Platform;

namespace Altinn.Studio.StudioctlServer.Discovery;

/// <summary>
/// Whether the discovered apps are registered with the localtest-v2 front door, and where it is: on while that runtime is
/// up (studioctl sets <c>Localtest:RegisterApps</c>), at the public origin <c>Localtest:Url</c> the storage probe uses.
/// </summary>
internal sealed record FrontDoorRegistrationOptions(Uri? PlatformUri, bool Enabled)
{
    public static FrontDoorRegistrationOptions FromConfiguration(IConfiguration configuration)
    {
        var platform = Uri.TryCreate(configuration["Localtest:Url"], UriKind.Absolute, out var uri) ? uri : null;
        var enabled = EnvironmentValues.IsTruthy(configuration["Localtest:RegisterApps"]);
        return new FrontDoorRegistrationOptions(platform, enabled && platform is not null);
    }
}

/// <summary>
/// Registers each discovered app with the localtest-v2 front door, the localtest container: one
/// <c>PUT localtest/api/apps/{org}/{app}</c> with the app's address as the container reaches it routes
/// <c>/{org}/{app}/</c> to the app and deploys it to the platform from the app's own API, as Altinn Studio deploys one;
/// <c>DELETE</c> deprovisions it when the app is gone. Apps started from an IDE are registered exactly like apps started
/// by <c>studioctl run</c>, since discovery finds both. Called from the registry's refresh, so one app at a time.
/// </summary>
internal sealed class FrontDoorRegistration
{
    public const string HttpClientName = "front-door-registration";

    /// <summary>How the front door, in a container, reaches the host: the runtime's host gateway.</summary>
    public const string HostGatewayName = "host.docker.internal";

    private const string AppsPath = "localtest/api/apps/";
    private const int MaxLoggedBody = 2000;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly FrontDoorRegistrationOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FrontDoorRegistration> _logger;
    private readonly Dictionary<string, Uri> _registered = new(StringComparer.OrdinalIgnoreCase);

    public FrontDoorRegistration(
        FrontDoorRegistrationOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<FrontDoorRegistration> logger
    )
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool Enabled => _options.Enabled;

    /// <summary>The app's address as the front door reaches it: its port on the host, through the host gateway.</summary>
    public static Uri FrontDoorUrl(DiscoveredApp app) =>
        new UriBuilder(Uri.UriSchemeHttp, HostGatewayName, app.BaseUri.Value.Port).Uri;

    /// <summary>
    /// Brings the front door in line with the discovered apps: a new app, or one that came back on another port, is
    /// registered (a failed registration is retried on the next refresh); an app that is gone is deregistered.
    /// </summary>
    public async Task Reconcile(IReadOnlyList<DiscoveredApp> apps, CancellationToken cancellationToken)
    {
        if (_options is not { Enabled: true, PlatformUri: { } platform })
            return;

        var current = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
            current.TryAdd(app.AppId, FrontDoorUrl(app));

        using var client = _httpClientFactory.CreateClient(HttpClientName);
        foreach (var (appId, url) in current)
        {
            if (_registered.TryGetValue(appId, out var known) && known == url)
                continue;
            if (await Register(client, platform, appId, url, cancellationToken))
                _registered[appId] = url;
        }

        foreach (var appId in _registered.Keys.Where(appId => !current.ContainsKey(appId)).ToArray())
        {
            await Deregister(client, platform, appId, cancellationToken);
            _registered.Remove(appId);
        }
    }

    private async Task<bool> Register(
        HttpClient client,
        Uri platform,
        string appId,
        Uri url,
        CancellationToken cancellationToken
    )
    {
        var endpoint = new Uri(platform, AppsPath + appId);
        try
        {
            using var response = await client.PutAsJsonAsync(
                endpoint,
                new RegistrationRequest(url.GetLeftPart(UriPartial.Authority)),
                _jsonOptions,
                cancellationToken
            );
            var body = await ReadBody(response, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation(
                        "Registered {AppId} with the localtest-v2 front door at {Url}: {Result}",
                        appId,
                        url,
                        body
                    );
                return true;
            }
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning(
                    "Registering {AppId} with the localtest-v2 front door at {Url} answered {StatusCode}: {Result}; retrying on the next refresh",
                    appId,
                    url,
                    (int)response.StatusCode,
                    body
                );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Registering {AppId} with the localtest-v2 front door at {Endpoint} failed; retrying on the next refresh",
                appId,
                endpoint
            );
        }
        return false;
    }

    private async Task Deregister(HttpClient client, Uri platform, string appId, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(platform, AppsPath + appId);
        try
        {
            using var response = await client.DeleteAsync(endpoint, cancellationToken);
            var body = await ReadBody(response, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("Removed {AppId} from the localtest-v2 front door", appId);
            }
            else if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning(
                    "Removing {AppId} from the localtest-v2 front door answered {StatusCode}: {Result}",
                    appId,
                    (int)response.StatusCode,
                    body
                );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Removing {AppId} from the localtest-v2 front door failed", appId);
        }
    }

    private static async Task<string> ReadBody(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        body = body.ReplaceLineEndings(" ");
        return body.Length <= MaxLoggedBody ? body : body[..MaxLoggedBody] + "...";
    }

    private sealed record RegistrationRequest(string Url);
}
