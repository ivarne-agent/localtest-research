using Altinn.Studio.Localtest.Provisioning;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Altinn.App.Aspire;

/// <summary>
/// Registers an app on the platform as Designer's deploy does (the shared provisioning library's
/// <see cref="LocaltestProvisioner" />: the Register's test users, Storage's metadata and texts, the PDP's policy, the
/// Resource Registry's subjects, Access Management, Dialogporten), from the app's files on disk. A failure is logged
/// with its step and URL and reported as a degraded health check of the app (the dashboard's warning state), not as a
/// failed start: the app runs, and the <c>provision</c> command runs it again.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "One per app resource, living as long as the AppHost."
)]
internal sealed class AppProvisioning
{
    /// <summary>The resource property that shows the last outcome in the dashboard.</summary>
    public const string StatusProperty = "altinn.provisioning";

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(100);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<bool> _dialogportenRunning;

    public AppProvisioning(AppId appId, DirectoryAppSource source, Uri platformUri, Func<bool> dialogportenRunning)
    {
        AppId = appId;
        Source = source;
        PlatformUri = platformUri;
        _dialogportenRunning = dialogportenRunning;
    }

    public AppId AppId { get; }

    public DirectoryAppSource Source { get; }

    public Uri PlatformUri { get; }

    /// <summary>The last outcome; <see cref="ProvisioningState.NotRun" /> until the first run.</summary>
    public ProvisioningStatus Status { get; private set; } = ProvisioningStatus.NotRun;

    /// <summary>Forgets the last outcome, when the app starts again.</summary>
    public void Reset() => Status = ProvisioningStatus.NotRun;

    /// <summary>Provisions the app; never throws but for cancellation. Returns the outcome.</summary>
    public Task<ProvisioningStatus> ProvisionAsync(
        IServiceProvider services,
        IResource resource,
        CancellationToken cancellationToken
    ) =>
        RunAsync(
            services,
            resource,
            "provision",
            async (provisioner, ct) =>
            {
                var result = await provisioner.Provision(AppId, Source, ct).ConfigureAwait(false);
                return $"provisioned {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}: {result.TextCount} text resource(s), "
                    + $"policy {result.HasPolicy}, test data {result.HasTestData}, Dialogporten {result.Dialogporten}";
            },
            cancellationToken
        );

    /// <summary>Removes the app's test users from the Register; never throws but for cancellation.</summary>
    public Task<ProvisioningStatus> DeprovisionAsync(
        IServiceProvider services,
        IResource resource,
        CancellationToken cancellationToken
    ) =>
        RunAsync(
            services,
            resource,
            "deprovision",
            async (provisioner, ct) =>
            {
                await provisioner.Deprovision(AppId, ct).ConfigureAwait(false);
                return $"deprovisioned {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}: the test users are removed";
            },
            cancellationToken
        );

    private async Task<ProvisioningStatus> RunAsync(
        IServiceProvider services,
        IResource resource,
        string operation,
        Func<LocaltestProvisioner, CancellationToken, Task<string>> run,
        CancellationToken cancellationToken
    )
    {
        var logger = services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
        var console = services.GetRequiredService<ILoggerFactory>().CreateLogger("Altinn.App.Aspire");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var handler = new SocketsHttpHandler();
            using var tracker = new RequestTracker(handler);
            using var client = new HttpClient(tracker, disposeHandler: false) { Timeout = _timeout };
            var provisioner = new LocaltestProvisioner(
                new LocaltestProvisionerOptions
                {
                    PlatformUri = PlatformUri,
                    DialogportenRunning = _dialogportenRunning,
                },
                client,
                logger
            );
            try
            {
                var message = await run(provisioner, cancellationToken).ConfigureAwait(false);
                Status = new ProvisioningStatus(ProvisioningState.Succeeded, message);
                console.LogProvisioned(AppId.ToString(), operation, message);
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                var step = ProvisioningSteps.Of(tracker.LastRequest);
                var url = tracker.LastRequest?.ToString() ?? Source.AppDirectory;
                Status = new ProvisioningStatus(
                    ProvisioningState.Failed,
                    $"{operation} failed at {step} ({url}): {exception.Message}"
                );
                logger.LogProvisioningFailed(exception, AppId.ToString(), operation, step, url);
                console.LogProvisioningFailed(exception, AppId.ToString(), operation, step, url);
            }
        }
        finally
        {
            _gate.Release();
        }
        await services
            .GetRequiredService<ResourceNotificationService>()
            .PublishUpdateAsync(
                resource,
                s =>
                    s with
                    {
                        Properties =
                        [
                            .. s.Properties.Where(p => p.Name != StatusProperty),
                            new ResourcePropertySnapshot(StatusProperty, Status.Message),
                        ],
                    }
            )
            .ConfigureAwait(false);
        return Status;
    }

    /// <summary>Records the last request, so a failure names the step and URL it failed at.</summary>
    private sealed class RequestTracker(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public Uri? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastRequest = request.RequestUri;
            return base.SendAsync(request, cancellationToken);
        }
    }
}

/// <summary>Where a provisioning stands.</summary>
internal enum ProvisioningState
{
    /// <summary>Not run since the app started.</summary>
    NotRun,

    /// <summary>The last run succeeded.</summary>
    Succeeded,

    /// <summary>The last run failed; the app runs anyway.</summary>
    Failed,
}

/// <summary>A provisioning's outcome.</summary>
/// <param name="State">Where it stands.</param>
/// <param name="Message">What happened, for the dashboard.</param>
internal sealed record ProvisioningStatus(ProvisioningState State, string Message)
{
    /// <summary>Not run yet.</summary>
    public static ProvisioningStatus NotRun { get; } = new(ProvisioningState.NotRun, "not provisioned yet");
}

/// <summary>The provisioning steps by the platform path they call (<c>localtest/README.md</c>, "What the app runner pushes").</summary>
internal static class ProvisioningSteps
{
    private static readonly (string Prefix, string Step)[] _steps =
    [
        ("/Home/GetTestStudioToken", "the Studio token (the localtest app)"),
        ("/register/", "step 1, the Register's test users"),
        ("/storage/", "step 2, Storage's application metadata and texts"),
        ("/authorization/", "step 3, the PDP's policy"),
        ("/resourceregistry/", "step 3b, the Resource Registry's policy subjects"),
        ("/accessmanagement/", "step 4, the Access Management refresh"),
        ("/dialogporten", "step 5, the Dialogporten sync"),
    ];

    /// <summary>The step a request belongs to; reading the app's files when no request was sent.</summary>
    public static string Of(Uri? request)
    {
        if (request is null)
        {
            return "reading the app's files";
        }
        var path = request.AbsolutePath;
        return _steps.FirstOrDefault(s => path.StartsWith(s.Prefix, StringComparison.OrdinalIgnoreCase)).Step
            ?? $"a request to {path}";
    }
}

/// <summary>
/// The app's provisioning as a health check: healthy until a run fails, then degraded with the step and URL, which the
/// dashboard shows as the app's warning state while the app keeps running.
/// </summary>
internal sealed class ProvisioningHealthCheck(AppProvisioning provisioning) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            provisioning.Status.State == ProvisioningState.Failed
                ? HealthCheckResult.Degraded(provisioning.Status.Message)
                : HealthCheckResult.Healthy(provisioning.Status.Message)
        );
}
