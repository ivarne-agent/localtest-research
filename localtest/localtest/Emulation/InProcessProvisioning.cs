using Localtest.Provisioning;

namespace Localtest.Emulated;

/// <summary>
/// The provisioning steps in minimal mode, service by service: the test data into the store (the emulated Register,
/// Profile, Authorization and Access Management read it on each request), the policy into memory, the Resource
/// Registry reload and the Access Management refresh skipped (nothing to derive or refresh). A step whose service is
/// not emulated goes over HTTP as in the default stack.
/// </summary>
public sealed class InProcessProvisioning(EmulationMode mode, TestDataStore store, PolicyStore policies, IHttpClientFactory httpClientFactory, ILogger<InProcessProvisioning> logger) : IProvisioningTarget
{
    private readonly HttpProvisioningTarget _http = new(httpClientFactory.CreateClient(AppProvisioning.PlatformClientName));

    public Task PutTestData(AppId appId, ReadOnlyMemory<byte>? testData, CancellationToken cancellationToken)
    {
        if (!mode.Emulates(RegisterEmulation.Service))
        {
            return _http.PutTestData(appId, testData, cancellationToken);
        }

        var data = store.Register(appId.Org, appId.App, testData);
        logger.LogInformation(
            "Test data for {AppId}: {Users} users from {Source}, {Conflicts} conflicts, {Warnings} warnings",
            appId.ToString(), data.Users.Count, data.Source, data.Conflicts.Count, data.Warnings.Count);
        return Task.CompletedTask;
    }

    public Task PutPolicy(AppId appId, string policy, string studioToken, CancellationToken cancellationToken)
    {
        if (!mode.Emulates(AuthorizationEmulation.Service))
        {
            return _http.PutPolicy(appId, policy, studioToken, cancellationToken);
        }

        policies.Set(appId.Org, appId.App, policy);
        return Task.CompletedTask;
    }

    public Task ReloadResourceSubjects(AppId appId, CancellationToken cancellationToken)
        => mode.Emulates(ResourceRegistryEmulation.Service) ? Task.CompletedTask : _http.ReloadResourceSubjects(appId, cancellationToken);

    public Task RefreshAccessManagement(CancellationToken cancellationToken)
        => mode.Emulates(AccessManagementEmulation.Service) ? Task.CompletedTask : _http.RefreshAccessManagement(cancellationToken);

    public Task RemoveTestData(AppId appId, CancellationToken cancellationToken)
    {
        if (!mode.Emulates(RegisterEmulation.Service))
        {
            return _http.RemoveTestData(appId, cancellationToken);
        }

        store.Unregister(appId.Org, appId.App);
        return Task.CompletedTask;
    }

    public Task RemovePolicy(AppId appId, CancellationToken cancellationToken)
    {
        if (mode.Emulates(AuthorizationEmulation.Service))
        {
            policies.Remove(appId.Org, appId.App);
        }

        return Task.CompletedTask;
    }
}
