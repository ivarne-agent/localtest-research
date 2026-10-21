#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Profile.Core.Integrations;
using Altinn.Profile.Core.OrganizationNotificationAddresses;
using Altinn.Profile.Integrations.OrganizationNotificationAddressRegistry;
using Altinn.Profile.Integrations.OrganizationNotificationAddressRegistry.Entities;
using Altinn.Profile.Integrations.OrganizationNotificationAddressRegistry.Models;
using Altinn.Profile.Integrations.Persistence;
using Altinn.Profile.Localtest.Register;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhoneNumbers;
using static Altinn.Profile.Integrations.OrganizationNotificationAddressRegistry.Models.DigitalContactPointModel;

namespace Altinn.Profile.Localtest.Brreg;

/// <summary>
/// Stands in for the contact register for organisations at brreg (KoFuVi: the <c>kof-reception</c> write API and the
/// <c>kof-sync</c> change log, both behind Maskinporten in production), the source of organisations' official
/// notification addresses. Profile's production code does the rest: <c>OrgSyncJob</c> →
/// <c>OrganizationNotificationAddressUpdateJob</c> → <c>OrganizationNotificationAddressRepository</c> writes the
/// mirror, and the address endpoints write through <see cref="IOrganizationNotificationAddressUpdateClient"/>.
/// <para>
/// Writes (endpoints <c>organizations/{orgNumber}/notificationaddresses/mandatory</c>) are accepted with a local registry
/// id; the production service stores the row with <c>has_registry_accepted = false</c>, which the lookups hide until the
/// registry's change log reports the address back. The change log built here does that, as brreg would, for every row
/// still waiting (accepted addresses, and soft-deleted ones as deletions), so a written address shows in the lookups after
/// the next organisation sync (every minute, or at once with <c>POST profile/api/localtest/sync</c>).
/// </para>
/// <para>
/// The seed: an organisation of the test data (the Register's <c>organizations</c>) with <c>notificationAddresses</c>
/// that Profile has no row for gets those addresses in the change log, once. From then on the organisation's addresses
/// are Profile's and user changes win; the seed never writes to an organisation Profile knows. Sub-units without
/// addresses get none, so Profile's main-unit fallback answers for them, as in production.
/// </para>
/// </summary>
public sealed class LocaltestOrganizationNotificationAddressRegistry(
    IDbContextFactory<ProfileDbContext> contextFactory,
    LocaltestRegisterTestData register,
    TimeProvider timeProvider,
    ILogger<LocaltestOrganizationNotificationAddressRegistry> logger)
    : IOrganizationNotificationAddressUpdateClient, IOrganizationNotificationAddressSyncClient
{
    private const string LocalChangeLog = "localtest:kofuvi/varslingsadresser";

    public Task<string> CreateNewNotificationAddress(NotificationAddress notificationAddress, string organizationNumber)
        => Task.FromResult(NewRegistryId());

    public Task<string> UpdateNotificationAddress(string registryId, NotificationAddress notificationAddress, string organizationNumber)
        => Task.FromResult(notificationAddress.IsSynthetic ? notificationAddress.NotificationAddressID.ToString() : registryId);

    public Task<string> DeleteNotificationAddress(NotificationAddress notificationAddress)
        => Task.FromResult(notificationAddress.IsSynthetic ? notificationAddress.NotificationAddressID.ToString() : notificationAddress.RegistryID);

    public string GetInitialUrl(DateTime? lastUpdated) => LocalChangeLog;

    /// <summary>One page: the seed for organisations Profile does not know, then the echo of Profile's own writes.</summary>
    public async Task<NotificationAddressChangesLog?> GetAddressChangesAsync(string endpointUrl, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entries = new List<Entry>();
        var known = (await db.Organizations.Select(o => o.RegistryOrganizationNumber).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        try
        {
            foreach (var org in await register.GetOrganizations(cancellationToken))
            {
                var addresses = org.NotificationAddresses ?? [];
                if (known.Contains(org.OrgNumber) || addresses.Count == 0)
                {
                    continue;
                }

                for (var i = 0; i < addresses.Count; i++)
                {
                    var contactPoint = ToContactPoint(addresses[i]);
                    if (contactPoint is null)
                    {
                        logger.LogWarning("Localtest: notification address {Index} of {OrgNumber} has neither an e-mail address nor a mobile number", i, org.OrgNumber);
                        continue;
                    }

                    entries.Add(Live($"lt-seed-{org.OrgNumber}-{i}", org.OrgNumber, contactPoint, now));
                }

                logger.LogInformation("Localtest: seeding the official notification addresses of {OrgNumber}", org.OrgNumber);
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Localtest: the Register's organisations did not answer; the notification address seed waits for the next sync");
        }

        var waiting = await db.NotificationAddresses
            .Include(a => a.Organization)
            .Where(a => a.HasRegistryAccepted == false || a.IsSoftDeleted == true)
            .ToListAsync(cancellationToken);
        foreach (var address in waiting.Where(a => !string.IsNullOrEmpty(a.RegistryID) && a.UpdateSource != UpdateSource.Synthetic))
        {
            entries.Add(address.IsSoftDeleted == true
                ? new Entry { Id = address.RegistryID, Updated = now, IsDeleted = true }
                : Live(address.RegistryID, address.Organization.RegistryOrganizationNumber, FromRow(address), now, address.NotificationName));
        }

        return new NotificationAddressChangesLog { OrganizationNotificationAddressList = entries, Updated = now, NextPage = null };
    }

    // brreg's ids fit Profile's registry_id column (varchar 32): "lt-" and 29 hex digits.
    private static string NewRegistryId() => "lt-" + Guid.NewGuid().ToString("N")[..29];

    private static Entry Live(string id, string orgNumber, DigitalContactPointModel contactPoint, DateTime now, string? title = null)
    {
        var content = new EntryContent
        {
            ContactPoint = new EntryContent.ContactPointModel
            {
                Id = id,
                DigitalContactPoint = contactPoint,
                UnitContactInfo = new UnitContactInfoModel
                {
                    UnitIdentifier = new UnitIdentifierModel { Type = DataMapper.OrganizationNumberType, Value = orgNumber },
                },
            },
        };
        return new Entry { Id = id, Title = title, Updated = now, IsDeleted = false, ContentStringified = JsonSerializer.Serialize(content) };
    }

    private static DigitalContactPointModel FromRow(NotificationAddressDE row)
        => row.AddressType == AddressType.Email
            ? new DigitalContactPointModel { EmailAddress = new EmailAddressModel { Username = row.Address, Domain = row.Domain } }
            : new DigitalContactPointModel { PhoneNumber = new PhoneNumberModel { Prefix = row.Domain, NationalNumber = row.Address, Name = row.NotificationName } };

    /// <summary>
    /// The test data's form (an e-mail address, or a mobile number in international form) as brreg reports it: user name
    /// and domain, or the international prefix and the national number.
    /// </summary>
    private static DigitalContactPointModel? ToContactPoint(LocaltestRegisterTestData.TestNotificationAddress address)
    {
        if (!string.IsNullOrWhiteSpace(address.Email))
        {
            var email = address.Email.Trim();
            var at = email.LastIndexOf('@');
            return new DigitalContactPointModel { EmailAddress = new EmailAddressModel { Username = email[..Math.Max(at, 0)], Domain = email[(at + 1)..] } };
        }

        if (!string.IsNullOrWhiteSpace(address.MobileNumber))
        {
            var number = address.MobileNumber.Trim();
            try
            {
                var parsed = PhoneNumberUtil.GetInstance().Parse(number, "NO");
                return new DigitalContactPointModel
                {
                    PhoneNumber = new PhoneNumberModel { Prefix = "+" + parsed.CountryCode, NationalNumber = parsed.NationalNumber.ToString() },
                };
            }
            catch (NumberParseException)
            {
                // Kept as written, as Profile's write API does with a number it cannot split.
                return new DigitalContactPointModel { PhoneNumber = new PhoneNumberModel { Prefix = null, NationalNumber = number } };
            }
        }

        return null;
    }
}
