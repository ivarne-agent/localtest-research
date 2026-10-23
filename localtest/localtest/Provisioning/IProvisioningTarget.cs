using System.Text;

namespace Localtest.Provisioning;

/// <summary>
/// Where the provisioning steps that are not Storage's push to: the Register's test data (step 1), the PDP's policy
/// store (step 3), the Resource Registry's subject reload (step 3b) and the Access Management refresh (step 4).
/// <see cref="HttpProvisioningTarget"/> calls the services over HTTP by name (the default stack). The minimal mode
/// (<c>Emulation/</c>) registers an in-process one that keeps the data in the emulated services' memory, service by
/// service; Storage's steps (metadata, texts) are HTTP in both modes. One interface rather than one per step: the step
/// list stays in one place (<see cref="LocaltestProvisioner"/>), and which services are emulated is the in-process
/// target's decision per call.
/// </summary>
public interface IProvisioningTarget
{
    /// <summary>Step 1: the app's test users as the app has them (null: the app has none).</summary>
    Task PutTestData(AppId appId, ReadOnlyMemory<byte>? testData, CancellationToken cancellationToken);

    /// <summary>Step 3: the app's policy, placeholders already replaced, with Designer's token.</summary>
    Task PutPolicy(AppId appId, string policy, string studioToken, CancellationToken cancellationToken);

    /// <summary>Step 3b: the Resource Registry derives the app's subjects from the pushed policy.</summary>
    Task ReloadResourceSubjects(AppId appId, CancellationToken cancellationToken);

    /// <summary>Step 4: Access Management refreshes its copy of parties, roles and resources.</summary>
    Task RefreshAccessManagement(CancellationToken cancellationToken);

    /// <summary>Deprovisioning: removes the app's test users (none registered is fine).</summary>
    Task RemoveTestData(AppId appId, CancellationToken cancellationToken);

    /// <summary>Deprovisioning: removes the app's policy where the target keeps it per registration (the PDP over HTTP keeps it).</summary>
    Task RemovePolicy(AppId appId, CancellationToken cancellationToken);
}

/// <summary>The provisioning steps over HTTP to each service by its name (what <see cref="LocaltestProvisioner"/> did inline before).</summary>
public sealed class HttpProvisioningTarget(HttpClient client) : IProvisioningTarget
{
    public Task PutTestData(AppId appId, ReadOnlyMemory<byte>? testData, CancellationToken cancellationToken)
        => PlatformHttp.Send(
            client,
            HttpMethod.Put,
            LocaltestProvisioner.Service("register", $"/register/api/localtest/apps/{appId.Org}/{appId.App}"),
            PlatformHttp.Json(testData),
            null,
            cancellationToken
        );

    public Task PutPolicy(AppId appId, string policy, string studioToken, CancellationToken cancellationToken)
        => PlatformHttp.Send(
            client,
            HttpMethod.Post,
            LocaltestProvisioner.Service("authorization", $"/authorization/api/v1/policies?org={Uri.EscapeDataString(appId.Org)}&app={Uri.EscapeDataString(appId.App)}"),
            new StringContent(policy, Encoding.UTF8, "application/xml"),
            studioToken,
            cancellationToken
        );

    // Anonymous endpoint; the app's own resource is computed from Storage's application list.
    public Task ReloadResourceSubjects(AppId appId, CancellationToken cancellationToken)
        => PlatformHttp.Send(
            client,
            HttpMethod.Get,
            LocaltestProvisioner.Service("resource-registry", $"/resourceregistry/api/v1/resource/app_{Uri.EscapeDataString(appId.Org)}_{Uri.EscapeDataString(appId.App)}/policy/subjects?reloadFromXacml=true"),
            null,
            null,
            cancellationToken
        );

    public Task RefreshAccessManagement(CancellationToken cancellationToken)
        => PlatformHttp.Send(
            client,
            HttpMethod.Post,
            LocaltestProvisioner.Service("access-management", "/accessmanagement/api/localtest/refresh"),
            null,
            null,
            cancellationToken
        );

    public Task RemoveTestData(AppId appId, CancellationToken cancellationToken)
        => PlatformHttp.Send(
            client,
            HttpMethod.Delete,
            LocaltestProvisioner.Service("register", $"/register/api/localtest/apps/{appId.Org}/{appId.App}"),
            null,
            null,
            cancellationToken,
            // The Register answers 404 for an app it has no test data for: nothing to remove, so a repeated or
            // never-provisioned removal succeeds (the front door's DELETE is idempotent).
            notFoundIsSuccess: true
        );

    // The PDP keeps the policy, as it keeps an app Designer no longer deploys.
    public Task RemovePolicy(AppId appId, CancellationToken cancellationToken) => Task.CompletedTask;
}
