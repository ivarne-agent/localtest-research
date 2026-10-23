using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Localtest.Provisioning;

/// <summary>
/// Deploys an app to the localtest platform the way Altinn Studio deploys one: the test users to the Register, the
/// application metadata and texts to Storage, the authorization policy to the PDP, the Resource Registry's subject
/// reload, the Access Management refresh, and, with the optional <c>dialogporten</c> group up, Dialogporten's sync of
/// the Resource Registry. Every call is plain HTTP to a service by its name (<c>http://storage/storage/api/v1/…</c>), which
/// the client's service-discovery handler resolves from <c>services__{name}__http__0</c>; every step is idempotent:
/// running it again replaces what the previous run pushed. One instance serves one platform; calls are not meant to run
/// concurrently. Copied from studioctl's provisioning library (see <see cref="AppId"/>).
/// </summary>
public sealed class LocaltestProvisioner
{
    private readonly LocaltestProvisionerOptions _options;
    private readonly HttpClient _client;
    private readonly IProvisioningTarget _target;
    private readonly ILogger _logger;

    // Whether the last Dialogporten sync failed: logged once, then retried quietly until it answers.
    private bool _dialogportenSyncFailing;

    /// <param name="options">The host's callbacks.</param>
    /// <param name="client">Used for the platform and, through the sources the caller passes, the app; not disposed.</param>
    /// <param name="logger">Receives the step results; the caller picks the category.</param>
    /// <param name="target">Where steps 1, 3, 3b and 4 go; default: the services over HTTP through <paramref name="client"/>.</param>
    public LocaltestProvisioner(LocaltestProvisionerOptions options, HttpClient client, ILogger logger, IProvisioningTarget? target = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _client = client;
        _target = target ?? new HttpProvisioningTarget(client);
        _logger = logger;
    }

    /// <summary>
    /// Reads the app's artefacts from <paramref name="source"/> and pushes them, steps 1 to 5 in order. A failing step
    /// throws (<see cref="HttpRequestException"/> with the method, URI, status and the start of the body) and leaves the
    /// later steps undone; run it again. Step 5's failure does not throw.
    /// </summary>
    public async Task<ProvisionResult> Provision(AppId appId, IAppSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (org, app) = (appId.Org, appId.App);

        var artifacts = await source.Read(appId, cancellationToken);
        var studioToken = await GetStudioToken(cancellationToken);

        // 1. Register: the app's test users as posted (an empty body: none).
        await _target.PutTestData(appId, artifacts.TestData, cancellationToken);

        // 2. Storage: application metadata and texts through the production endpoints, created or replaced.
        await PlatformHttp.CreateOrReplace(
            _client,
            Service("storage", $"/storage/api/v1/applications?appId={org}/{app}"),
            Service("storage", $"/storage/api/v1/applications/{org}/{app}"),
            () => PlatformHttp.Json(artifacts.ApplicationMetadata),
            studioToken,
            cancellationToken
        );
        foreach (var (language, text) in artifacts.Texts)
        {
            await PlatformHttp.CreateOrReplace(
                _client,
                Service("storage", $"/storage/api/v1/applications/{org}/{app}/texts"),
                Service("storage", $"/storage/api/v1/applications/{org}/{app}/texts/{Uri.EscapeDataString(language)}"),
                () => PlatformHttp.Json(text),
                studioToken,
                cancellationToken
            );
        }

        // 3. The PDP: the policy with Designer's placeholder replacement.
        if (artifacts.Policy is { } policy && !string.IsNullOrWhiteSpace(policy))
        {
            await _target.PutPolicy(appId, PolicyPlaceholders.Replace(policy, appId), studioToken, cancellationToken);

            // 3b. The Resource Registry derives the app's subjects from the pushed policy, as Designer does on deploy.
            await _target.ReloadResourceSubjects(appId, cancellationToken);
        }

        // 4. Access Management keeps its own copy of parties, roles and resources.
        await _target.RefreshAccessManagement(cancellationToken);

        // 5. Dialogporten (optional group): the Janitor's two Resource Registry syncs and a cache clear, so the app's
        // resource and subjects are known at once instead of after the 20-minute resource cache. After step 4 because the
        // clear also drops the authorized-parties cache, which then reads the test users Access Management has just
        // refreshed; after 3b because the subject sync reads what the registry derived from the policy.
        var dialogporten = await SyncDialogporten(appId.ToString(), cancellationToken);

        var result = new ProvisionResult(
            artifacts.Texts.Count,
            artifacts.Policy is not null,
            artifacts.TestData is not null,
            dialogporten
        );
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation(
                "Provisioned {AppId} from {Source}: metadata, {TextCount} text resource(s), policy {HasPolicy}, test data {HasTestData}",
                appId.ToString(),
                source.ToString(),
                result.TextCount,
                result.HasPolicy,
                result.HasTestData
            );
        return result;
    }

    /// <summary>
    /// Removes the app's test users from the Register (none registered is fine) and refreshes Access Management. Storage, the policy store and
    /// the Resource Registry keep the app, as they keep an app Designer no longer deploys.
    /// </summary>
    public async Task Deprovision(AppId appId, CancellationToken cancellationToken)
    {
        await _target.RemoveTestData(appId, cancellationToken);
        await _target.RemovePolicy(appId, cancellationToken);
        await _target.RefreshAccessManagement(cancellationToken);
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Removed {AppId}'s test users from the platform", appId.ToString());
    }

    /// <summary>
    /// Step 5 on its own: Dialogporten's localtest sync when <see cref="LocaltestProvisionerOptions.DialogportenRunning"/>
    /// says the group is up (logged as skipped otherwise). A runner calls it again when the group comes up after the
    /// apps were provisioned, or after a <see cref="DialogportenSync.Failed"/>. Never throws for a failed sync, since
    /// Dialogporten is optional; the first failure in a row is logged as a warning.
    /// </summary>
    /// <param name="reason">What the sync is for, in the log: an app id or a description.</param>
    /// <param name="cancellationToken">Cancels the sync; cancellation is thrown, not reported.</param>
    public async Task<DialogportenSync> SyncDialogporten(string reason, CancellationToken cancellationToken)
    {
        if (!_options.DialogportenRunning())
        {
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation(
                    "Dialogporten sync for {Reason} skipped: the dialogporten group is not running",
                    reason
                );
            return DialogportenSync.Skipped;
        }

        var uri = Service("dialogporten-webapi", "/dialogporten/api/v1/localtest/sync");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            using var response = await PlatformHttp.SendAsync(_client, request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"POST {uri} returned {(int)response.StatusCode}: {PlatformHttp.Truncate(body)}"
                );
            _dialogportenSyncFailing = false;
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation(
                    "Dialogporten synced the Resource Registry for {Reason}: {Summary}",
                    reason,
                    DialogportenSyncSummary(body)
                );
            return DialogportenSync.Synced;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Dialogporten is optional: any failure is reported and retried, never thrown.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            if (!_dialogportenSyncFailing)
                _logger.LogWarning(
                    ex,
                    "Dialogporten sync for {Reason} failed; retrying on the next refresh (by hand: POST {Uri})",
                    reason,
                    uri
                );
            _dialogportenSyncFailing = true;
            return DialogportenSync.Failed;
        }
    }

    private Task<string> GetStudioToken(CancellationToken cancellationToken) => _options.StudioToken(cancellationToken);

    /// <summary>A platform service's URI by name, for the client's service-discovery handler to resolve.</summary>
    internal static Uri Service(string service, string pathAndQuery) => new($"http://{service}{pathAndQuery}");

    private static string DialogportenSyncSummary(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            return $"{root.GetProperty("subjectResources").GetInt32()} subject-resource pair(s), "
                + $"{root.GetProperty("resourcePolicyInformation").GetInt32()} resource policies, caches cleared";
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return PlatformHttp.Truncate(body);
        }
    }
}
