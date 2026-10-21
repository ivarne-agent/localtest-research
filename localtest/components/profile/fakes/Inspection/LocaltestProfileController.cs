#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Profile.Integrations.Persistence;
using Altinn.Profile.Localtest.Register;
using Altinn.Profile.Localtest.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Altinn.Profile.Localtest.Inspection;

/// <summary>
/// Localtest-only, anonymous inspection API (<c>profile/api/localtest/</c>): what the seed and the syncs have written and
/// what a user has stored, without a token, and a way to run the seed now instead of waiting for the jobs' next minute.
/// Production has nothing like it; nothing here is a Profile contract.
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("profile/api/localtest")]
public sealed class LocaltestProfileController(
    IDbContextFactory<ProfileDbContext> contextFactory,
    LocaltestSync sync,
    LocaltestRegisterTestData register) : ControllerBase
{
    /// <summary>Applied migrations, the KRR change number, the organisation sync watermark and row counts.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var krr = await db.Metadata.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var orgSync = await db.RegistrySyncMetadata.AsNoTracking().OrderByDescending(m => m.LastChangedDateTime).FirstOrDefaultAsync(cancellationToken);
        return Ok(new
        {
            migrations = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList(),
            pendingMigrations = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList(),
            krr = new { latestChangeNumber = krr?.LatestChangeNumber ?? 0, exported = krr?.Exported, persons = await db.People.CountAsync(cancellationToken) },
            organizationNotificationAddresses = new
            {
                lastSync = orgSync?.LastChangedDateTime,
                organizations = await db.Organizations.CountAsync(cancellationToken),
                addresses = await db.NotificationAddresses.CountAsync(a => a.IsSoftDeleted != true, cancellationToken),
                awaitingRegistry = await db.NotificationAddresses.CountAsync(a => a.HasRegistryAccepted == false || a.IsSoftDeleted == true, cancellationToken),
            },
            profileSettings = await db.ProfileSettings.CountAsync(cancellationToken),
        });
    }

    /// <summary>Runs the KRR sync, the organisation address sync and the settings seed now, in that order.</summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken cancellationToken)
    {
        var steps = await sync.RunAll(cancellationToken);
        return Ok(new { steps });
    }

    /// <summary>
    /// A user's rows: settings, the KRR mirror row (by the user's national identity number from the Register's test
    /// data), the self-identified contact row, favourites and groups, professional notification addresses, verified
    /// addresses and open verification codes (without the code).
    /// </summary>
    [HttpGet("users/{userId:int}")]
    public async Task<IActionResult> GetUser(int userId, CancellationToken cancellationToken)
    {
        var ssn = (await register.GetUsers(cancellationToken)).FirstOrDefault(u => u.UserId == userId)?.Ssn;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var person = string.IsNullOrEmpty(ssn) ? null : await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.FnumberAk == ssn, cancellationToken);
        return Ok(new
        {
            userId,
            profileSettings = await db.ProfileSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken),
            krr = person is null ? null : new
            {
                nationalIdentityNumber = person.FnumberAk,
                reserved = person.Reservation,
                email = person.EmailAddress,
                emailLastUpdated = person.EmailAddressLastUpdated,
                mobileNumber = person.MobilePhoneNumber,
                mobileNumberLastUpdated = person.MobilePhoneNumberLastUpdated,
            },
            selfIdentified = await db.SelfIdentifiedUsers.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken),
            partyGroups = await db.Groups.AsNoTracking().Where(g => g.UserId == userId)
                .Select(g => new { g.GroupId, g.Name, g.IsFavorite, parties = g.Parties.Select(p => p.PartyUuid).ToList() })
                .ToListAsync(cancellationToken),
            professionalNotificationAddresses = await db.UserPartyContactInfo.AsNoTracking().Where(c => c.UserId == userId)
                .Select(c => new { c.PartyUuid, c.EmailAddress, c.PhoneNumber, c.LastChanged, resources = c.UserPartyContactInfoResources!.Select(r => r.ResourceId).ToList() })
                .ToListAsync(cancellationToken),
            verifiedAddresses = await db.VerifiedAddresses.AsNoTracking().Where(v => v.UserId == userId)
                .Select(v => new { v.Address, addressType = v.AddressType.ToString(), v.VerifiedAt, verificationType = v.VerificationType.ToString() })
                .ToListAsync(cancellationToken),
            openVerificationCodes = await db.VerificationCodes.AsNoTracking().Where(v => v.UserId == userId)
                .Select(v => new { v.Address, addressType = v.AddressType.ToString(), v.Created, v.Expires, v.FailedAttempts })
                .ToListAsync(cancellationToken),
        });
    }

    /// <summary>An organisation's notification address rows, with where each came from and whether the registry accepted it.</summary>
    [HttpGet("organizations/{orgNumber}")]
    public async Task<IActionResult> GetOrganization(string orgNumber, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var org = await db.Organizations.AsNoTracking().Include(o => o.NotificationAddresses)
            .FirstOrDefaultAsync(o => o.RegistryOrganizationNumber == orgNumber, cancellationToken);
        if (org is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            organizationNumber = org.RegistryOrganizationNumber,
            notificationAddresses = org.NotificationAddresses.OrderBy(a => a.NotificationAddressID).Select(a => new
            {
                notificationAddressId = a.NotificationAddressID,
                addressType = a.AddressType.ToString(),
                fullAddress = a.FullAddress,
                registryId = a.RegistryID,
                updateSource = a.UpdateSource.ToString(),
                hasRegistryAccepted = a.HasRegistryAccepted,
                isSoftDeleted = a.IsSoftDeleted,
            }),
        });
    }
}
