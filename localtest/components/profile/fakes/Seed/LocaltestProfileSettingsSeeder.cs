#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Authorization.ServiceDefaults.Jobs;
using Altinn.Profile.Core.Integrations;
using Altinn.Profile.Core.User.ProfileSettings;
using Altinn.Profile.Integrations.Persistence;
using Altinn.Profile.Localtest.Register;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Altinn.Profile.Localtest.Seed;

/// <summary>
/// Writes the test users' profile settings (<c>user_preferences.profile_settings</c>: language, "do not prompt for
/// party", the preselected party) from the test data, which production gets from the users themselves (Arbeidsflate's
/// profile page, the Altinn 2 portal before it). A user is seeded once: only a user without a row gets one, so a setting
/// the user changed later always wins. The rest of a new row is Profile's defaults.
/// </summary>
public sealed class LocaltestProfileSettingsSeeder(
    LocaltestRegisterTestData register,
    IProfileSettingsRepository repository,
    IDbContextFactory<ProfileDbContext> contextFactory,
    ILogger<LocaltestProfileSettingsSeeder> logger)
{
    /// <summary>Seeds every test user without settings; returns how many rows were written.</summary>
    public async Task<int> SeedAsync(CancellationToken cancellationToken)
    {
        var users = await register.GetUsers(cancellationToken);
        if (users.Count == 0)
        {
            return 0;
        }

        // The preselected party is stored by UUID; the test data names it by party id. Every party a user can preselect
        // is a test person's own party or a test organisation.
        var partyUuids = new Dictionary<int, Guid>();
        foreach (var user in users)
        {
            partyUuids[user.PartyId] = user.PartyUuid;
        }

        foreach (var org in await register.GetOrganizations(cancellationToken))
        {
            partyUuids[org.PartyId] = org.PartyUuid;
        }

        HashSet<int> seeded;
        await using (var db = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            var ids = users.Select(u => u.UserId).ToList();
            seeded = [.. await db.ProfileSettings.Where(s => ids.Contains(s.UserId)).Select(s => s.UserId).ToListAsync(cancellationToken)];
        }

        var written = 0;
        foreach (var user in users.Where(u => !seeded.Contains(u.UserId)))
        {
            var settings = ProfileSettings.GetDefaultValues();
            settings.UserId = user.UserId;
            settings.LanguageType = user.Profile?.Language is { Length: > 0 } language ? language : LanguageType.NB;
            settings.DoNotPromptForParty = user.Profile?.DoNotPromptForParty ?? false;
            if (user.Profile is { PreSelectedPartyId: > 0 } profile)
            {
                if (partyUuids.TryGetValue(profile.PreSelectedPartyId, out var uuid))
                {
                    settings.PreselectedPartyUuid = uuid;
                }
                else
                {
                    logger.LogWarning("Localtest: preselected party {PartyId} of user {UserId} is not in the test data; not set", profile.PreSelectedPartyId, user.UserId);
                }
            }

            try
            {
                await repository.UpdateProfileSettings(settings, cancellationToken);
                written++;
            }
            catch (DbUpdateException ex)
            {
                // The user wrote settings between the read above and this insert: theirs win.
                logger.LogInformation(ex, "Localtest: user {UserId} got settings meanwhile; not seeded", user.UserId);
            }
        }

        if (written > 0)
        {
            logger.LogInformation("Localtest: profile settings seeded for {Count} test users", written);
        }

        return written;
    }
}

/// <summary>Runs <see cref="LocaltestProfileSettingsSeeder"/> on the jobs' schedule, so users of apps' test data get theirs too.</summary>
public sealed class LocaltestProfileSettingsSeedJob(LocaltestProfileSettingsSeeder seeder) : Job
{
    protected override Task RunAsync(CancellationToken cancellationToken = default)
        => seeder.SeedAsync(cancellationToken);
}
