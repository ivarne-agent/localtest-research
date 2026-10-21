#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Authorization.ServiceDefaults.Leases;
using Altinn.Profile.Integrations.ContactRegister;
using Altinn.Profile.Integrations.OrganizationNotificationAddressRegistry;
using Altinn.Profile.Jobs;
using Altinn.Profile.Localtest.Seed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Altinn.Profile.Localtest.Startup;

/// <summary>
/// Runs the seed on demand (at start and from <c>POST profile/api/localtest/sync</c>): the production KRR sync and
/// organisation address sync, each under the lease its recurring job takes, so a manual run never overlaps the job, then
/// the profile settings seed. The recurring jobs run the same three every minute.
/// </summary>
public sealed class LocaltestSync(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<LocaltestSync> logger)
{
    /// <summary>The lease of <see cref="LocaltestProfileSettingsSeedJob"/>.</summary>
    public const string SettingsSeedLease = "localtest-profile-settings-seed";

    private static readonly TimeSpan LeaseWait = TimeSpan.FromSeconds(30);

    /// <summary>The three steps in order; a failing step is logged and reported, and the next one still runs.</summary>
    public async Task<IReadOnlyList<SyncStep>> RunAll(CancellationToken cancellationToken)
    {
        return
        [
            await Step("krr", JobLeaseNames.KrrSyncJob, sp => sp.GetRequiredService<IContactRegisterUpdateJob>().SyncContactInformationAsync(), cancellationToken),
            await Step("organizationNotificationAddresses", JobLeaseNames.OrgSyncJob, sp => sp.GetRequiredService<IOrganizationNotificationAddressSyncJob>().SyncNotificationAddressesAsync(cancellationToken), cancellationToken),
            await Step("profileSettings", SettingsSeedLease, sp => sp.GetRequiredService<LocaltestProfileSettingsSeeder>().SeedAsync(cancellationToken), cancellationToken),
        ];
    }

    private async Task<SyncStep> Step(string name, string leaseName, Func<IServiceProvider, Task> run, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var leases = scope.ServiceProvider.GetRequiredService<LeaseManager>();
            var deadline = started + LeaseWait;
            while (true)
            {
                await using var lease = await leases.AcquireLease(leaseName, cancellationToken);
                if (lease.Acquired)
                {
                    await run(scope.ServiceProvider);
                    break;
                }

                if (timeProvider.GetUtcNow() > deadline)
                {
                    return new SyncStep(name, "skipped", $"the lease {leaseName} stayed taken (the recurring job is running)");
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            return new SyncStep(name, "ok", null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Localtest: {Step} sync failed", name);
            return new SyncStep(name, "failed", ex.Message);
        }
    }

    /// <summary>One step of a run: its name, <c>ok</c>, <c>skipped</c> or <c>failed</c>, and why when not ok.</summary>
    public sealed record SyncStep(string Step, string Outcome, string? Detail);
}
