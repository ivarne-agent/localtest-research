using Altinn.Studio.EnvTopology;
using Altinn.Studio.Localtest.Provisioning;
using Altinn.Studio.StudioctlServer.Discovery;
using Altinn.Studio.StudioctlServer.Proxy;

namespace Altinn.Studio.StudioctlServer.Provisioning;

/// <summary>
/// Where the provisioner finds the platform: the public origin studioctl-server itself proxies, the same
/// <c>Localtest:Url</c> the storage probe uses. Provisioning is on only when the proxy is, which is when the
/// localtest-v2 runtime is up.
/// </summary>
internal sealed record AppProvisionerOptions(Uri? PlatformUri, bool Enabled)
{
    public static AppProvisionerOptions FromConfiguration(IConfiguration configuration)
    {
        var proxy = ProxyOptions.FromConfiguration(configuration);
        var platform = Uri.TryCreate(configuration["Localtest:Url"], UriKind.Absolute, out var uri) ? uri : null;
        return new AppProvisionerOptions(platform, proxy.Enabled && platform is not null);
    }
}

/// <summary>
/// Deploys each discovered app to the localtest-v2 platform the way Altinn Studio deploys one. The steps (test users to
/// the Register, metadata and texts to Storage, the policy to the PDP, the Resource Registry's subject reload, Access
/// Management's refresh, and Dialogporten's sync when its group is up) are the shared
/// <see cref="LocaltestProvisioner"/>'s, which the Aspire package uses too; this adapter keeps what is studioctl-server's:
/// which apps exist (the registry's discovery), where they listen (read through their own API, since discovery knows
/// only the address, so apps started from an IDE are provisioned exactly like apps started by `studioctl run`), and
/// whether the Dialogporten group is up (the route table).
/// </summary>
internal sealed class AppProvisioner
{
    public const string HttpClientName = "app-provisioner";

    /// <summary>
    /// The Dialogporten WebApi's component in studioctl's manifest (`internal/localtestv2/manifest/manifest.json`); its
    /// route is enabled exactly when `env up --with dialogporten` turned the group on.
    /// </summary>
    internal const string DialogportenComponent = "dialogporten-webapi";

    private readonly AppProvisionerOptions _options;
    private readonly HttpClient _client;
    private readonly BoundTopologyIndexAccessor _topology;
    private readonly ILogger<AppProvisioner> _logger;
    private readonly LocaltestProvisioner? _provisioner;
    private readonly Dictionary<string, Uri> _provisioned = new(StringComparer.OrdinalIgnoreCase);

    // Whether the running Dialogporten has synced since the last app was provisioned.
    private bool _dialogportenSynced;

    public AppProvisioner(
        AppProvisionerOptions options,
        IHttpClientFactory httpClientFactory,
        BoundTopologyIndexAccessor topology,
        ILogger<AppProvisioner> logger
    )
    {
        _options = options;
        _client = httpClientFactory.CreateClient(HttpClientName);
        _topology = topology;
        _logger = logger;
        if (options is { Enabled: true, PlatformUri: { } platform })
            _provisioner = new LocaltestProvisioner(
                new LocaltestProvisionerOptions
                {
                    PlatformUri = platform,
                    DialogportenRunning = () => DialogportenRunning(_topology.Current),
                },
                _client,
                logger
            );
    }

    public bool Enabled => _options.Enabled;

    /// <summary>
    /// Brings the platform in line with the discovered apps: a new app, or one that came back on another
    /// port, is provisioned; an app that is gone has its test users removed. Called from the registry's
    /// refresh, so one app at a time and never concurrently.
    /// </summary>
    public async Task Reconcile(IReadOnlyList<DiscoveredApp> apps, CancellationToken cancellationToken)
    {
        if (_provisioner is null)
            return;

        var current = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
            current.TryAdd(app.AppId, app.BaseUri.Value);

        foreach (var (appId, baseUri) in current)
        {
            if (_provisioned.TryGetValue(appId, out var known) && known == baseUri)
                continue;

            try
            {
                var result = await _provisioner.Provision(
                    AppId.Parse(appId),
                    new RunningAppSource(_client, baseUri),
                    cancellationToken
                );
                _dialogportenSynced = result.Dialogporten == DialogportenSync.Synced;
                _provisioned[appId] = baseUri;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Provisioning {AppId} from {BaseUri} failed; retrying on the next refresh",
                    appId,
                    baseUri
                );
            }
        }

        foreach (var appId in _provisioned.Keys.Where(appId => !current.ContainsKey(appId)).ToArray())
        {
            try
            {
                await _provisioner.Deprovision(AppId.Parse(appId), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Removing {AppId} from the platform failed", appId);
            }
            _provisioned.Remove(appId);
        }

        // The group can come up after the apps were provisioned (`env up --with dialogporten` on a running environment),
        // and the first sync can find Dialogporten still starting: either way the next refresh syncs.
        if (!DialogportenRunning(_topology.Current))
            _dialogportenSynced = false;
        else if (!_dialogportenSynced && _provisioned.Count > 0)
            _dialogportenSynced =
                await _provisioner.SyncDialogporten("the provisioned apps", cancellationToken)
                == DialogportenSync.Synced;
    }

    /// <summary>
    /// Whether the `dialogporten` group is up, read from the route table `env up` writes together with the state file's
    /// group list (and which this server already watches for the proxy): the WebApi's route is enabled exactly when the
    /// group is on (with the group off it is written disabled, so the proxy can answer 503), so there is no probe and no
    /// second copy of the group list.
    /// </summary>
    internal static bool DialogportenRunning(BoundTopologyIndex topology) =>
        topology.TryGetComponentRoute(DialogportenComponent) is { Route.Enabled: true };
}
