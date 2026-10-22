using Localtest.Home;
using Localtest.Proxy;

namespace Localtest.Provisioning;

/// <summary>
/// Deploys a registered app to the platform the way Altinn Studio deploys one, from the running app's own API, and
/// removes its test users again; the steps are <see cref="LocaltestProvisioner"/>'s. Platform calls go by service name
/// through the resolving factory client; the app is reached at the address the runner gave, through a plain client
/// (a name the configuration does not know does not resolve). One provisioning at a time, as the provisioner expects.
/// </summary>
public sealed class AppProvisioning : IDisposable
{
    /// <summary>The factory client for the platform calls (service discovery, as every factory client here).</summary>
    public const string PlatformClientName = "localtest-provisioning";

    private readonly LocaltestProvisioner _provisioner;
    private readonly HttpClient _appClient;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AppProvisioning(IHttpClientFactory httpClientFactory, StudioToken studioToken, IConfiguration configuration, ILogger<AppProvisioning> logger)
    {
        _appClient = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(5) })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _provisioner = new LocaltestProvisioner(
            new LocaltestProvisionerOptions
            {
                StudioToken = async cancellationToken =>
                {
                    var (token, error, _) = await studioToken.Get(cancellationToken);
                    return token ?? throw new HttpRequestException($"No Studio token: {error}");
                },
                // Dialogporten runs when its adapter has an address: the adapter is what turns instances into dialogs.
                DialogportenRunning = () => !string.IsNullOrEmpty(configuration["services:dialogporten-adapter:http:0"]),
            },
            httpClientFactory.CreateClient(PlatformClientName),
            logger);
    }

    /// <summary>Provisions <c>{org}/{app}</c> from the app at <paramref name="url"/>; the outcome, never an exception but cancellation.</summary>
    public async Task<ProvisioningStatus> Provision(string org, string app, string url, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var result = await _provisioner.Provision(AppId.Parse($"{org}/{app}"), new RunningAppSource(_appClient, new Uri(url)), cancellationToken);
            return ProvisioningStatus.Succeeded(result);
        }
        catch (Exception ex) when (Failed(ex, cancellationToken))
        {
            return ProvisioningStatus.Failed(ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Removes the app's test users from the Register and refreshes Access Management; the error, or null.</summary>
    public async Task<string?> Deprovision(string org, string app, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await _provisioner.Deprovision(AppId.Parse($"{org}/{app}"), cancellationToken);
            return null;
        }
        catch (Exception ex) when (Failed(ex, cancellationToken))
        {
            return ProvisioningStatus.Describe(ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _appClient.Dispose();
        _lock.Dispose();
    }

    private static bool Failed(Exception ex, CancellationToken cancellationToken)
        => !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested);
}

/// <summary>The outcome of an app's last provisioning: <c>succeeded</c> with what was pushed, or <c>failed</c> with why.</summary>
public sealed record ProvisioningStatus(string State, DateTimeOffset At, ProvisionResult? Result, string? Error)
{
    public static ProvisioningStatus Succeeded(ProvisionResult result) => new("succeeded", DateTimeOffset.UtcNow, result, null);

    public static ProvisioningStatus Failed(Exception exception) => new("failed", DateTimeOffset.UtcNow, null, Describe(exception));

    /// <summary>The failure in one line: the failing request and its answer, or why it was not sent.</summary>
    public static string Describe(Exception exception) => ServiceCalls.NoAddress(exception)
        ? $"{exception.Message} (the service has no address: services__<name>__http__0 is not set, so it is not running)"
        : exception.Message;
}
