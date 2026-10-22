using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Altinn.App.Aspire;

/// <summary>
/// Registers an app with the front door, which routes it and provisions it from the app's own API (test users,
/// metadata, texts, policy, Access Management, Dialogporten when its adapter runs): one
/// <c>PUT localtest/api/apps/{org}/{app}</c> with the app's address, as studioctl-server does for the apps it
/// discovers. A failure is logged with the front door's answer and reported as a degraded health check of the app (the
/// dashboard's warning state), not as a failed start: the app runs, and the <c>provision</c> command registers it again.
/// </summary>
internal sealed class AppProvisioning
{
    /// <summary>The resource property that shows the last outcome in the dashboard.</summary>
    public const string StatusProperty = "altinn.provisioning";

    private readonly Func<int> _appPort;

    /// <param name="appId">The app.</param>
    /// <param name="platformUri">The front door.</param>
    /// <param name="appPort">The app's port on the host, once Aspire allocated it.</param>
    public AppProvisioning(AppId appId, Uri platformUri, Func<int> appPort)
    {
        AppId = appId;
        PlatformUri = platformUri;
        _appPort = appPort;
    }

    public AppId AppId { get; }

    public Uri PlatformUri { get; }

    /// <summary>The last outcome; <see cref="ProvisioningState.NotRun" /> until the first call.</summary>
    public ProvisioningStatus Status { get; private set; } = ProvisioningStatus.NotRun;

    /// <summary>Forgets the last outcome, when the app starts again.</summary>
    public void Reset() => Status = ProvisioningStatus.NotRun;

    /// <summary>Registers the app (route and provisioning); never throws but for cancellation.</summary>
    public Task<ProvisioningStatus> ProvisionAsync(
        IServiceProvider services,
        IResource resource,
        CancellationToken cancellationToken
    ) =>
        RunAsync(
            services,
            resource,
            "provision",
            ct =>
                FrontDoorClient.PutAsync(
                    PlatformUri,
                    FrontDoorClient.AppPath(AppId),
                    FrontDoorClient.HostAddress(_appPort()),
                    ct
                ),
            cancellationToken
        );

    /// <summary>Deregisters the app (test users and route removed); never throws but for cancellation.</summary>
    public Task<ProvisioningStatus> DeprovisionAsync(
        IServiceProvider services,
        IResource resource,
        CancellationToken cancellationToken
    ) =>
        RunAsync(
            services,
            resource,
            "deprovision",
            ct => FrontDoorClient.DeleteAsync(PlatformUri, FrontDoorClient.AppPath(AppId), ct),
            cancellationToken
        );

    private async Task<ProvisioningStatus> RunAsync(
        IServiceProvider services,
        IResource resource,
        string operation,
        Func<CancellationToken, Task<FrontDoorAnswer>> call,
        CancellationToken cancellationToken
    )
    {
        var logger = services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
        var console = services.GetRequiredService<ILoggerFactory>().CreateLogger("Altinn.App.Aspire");
        var app = AppId.ToString();
        try
        {
            var answer = await call(cancellationToken).ConfigureAwait(false);
            var message = $"{operation} {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}: {answer}";
            Status = new ProvisioningStatus(
                answer.Succeeded ? ProvisioningState.Succeeded : ProvisioningState.Failed,
                message
            );
            if (answer.Succeeded)
            {
                logger.LogProvisioned(app, operation, answer.ToString());
                console.LogProvisioned(app, operation, answer.ToString());
            }
            else
            {
                logger.LogProvisioningRefused(app, operation, answer.ToString());
                console.LogProvisioningRefused(app, operation, answer.ToString());
            }
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var url = new Uri(PlatformUri, FrontDoorClient.AppPath(AppId));
            Status = new ProvisioningStatus(
                ProvisioningState.Failed,
                $"{operation} failed ({url}): {exception.Message}"
            );
            logger.LogProvisioningFailed(exception, app, operation, url.ToString());
            console.LogProvisioningFailed(exception, app, operation, url.ToString());
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
}

/// <summary>Where a provisioning stands.</summary>
internal enum ProvisioningState
{
    /// <summary>Not run since the app started.</summary>
    NotRun,

    /// <summary>The last call succeeded.</summary>
    Succeeded,

    /// <summary>The last call failed; the app runs anyway.</summary>
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

/// <summary>
/// The app's provisioning as a health check: healthy until a call fails, then degraded with the front door's answer,
/// which the dashboard shows as the app's warning state while the app keeps running.
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
