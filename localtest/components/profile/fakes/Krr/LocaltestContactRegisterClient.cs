#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Profile.Integrations.ContactRegister;
using Altinn.Profile.Integrations.ContactRegister.Models;
using Altinn.Profile.Localtest.Register;
using Microsoft.Extensions.Logging;

namespace Altinn.Profile.Localtest.Krr;

/// <summary>
/// Stands in for KRR's change log (<c>hentEndringer</c>, read with a Maskinporten token in production): the test
/// persons' e-mail, mobile number and reservation as the Register's KRR stand-in answers them, presented as a change log
/// so that the production <c>KrrSyncJob</c> → <c>ContactRegisterUpdateJob</c> → <c>PersonRepository</c> write
/// <c>contact_and_reservation.person</c>, the mirror every user profile and contact point lookup reads.
/// <para>
/// The whole population is one change, numbered one past the change number Profile has stored, and it is emitted only
/// when its content differs from the last change this process emitted and Profile stored (a restart emits it once more,
/// which the production upsert absorbs). "Last updated" is the time of the change, so the 18-month staleness rule of the
/// contact point lookups never hides a test person. Persons who leave the test data (an app's data replacing the seed)
/// keep their row, as a person who leaves KRR's change log does. Empty strings from the stand-in become absent values.
/// </para>
/// </summary>
public sealed class LocaltestContactRegisterClient(
    LocaltestRegisterTestData register,
    TimeProvider timeProvider,
    ILogger<LocaltestContactRegisterClient> logger) : IContactRegisterHttpClient
{
    private static readonly Lock Gate = new();
    private static (long ChangeNumber, string Hash)? _emitted;

    public async Task<ContactRegisterChangesLog?> GetContactDetailsChangesAsync(string endpointUrl, long startingIdentifier)
    {
        ImmutableList<PersonContactPreferencesSnapshot> persons;
        try
        {
            var users = await register.GetUsers(CancellationToken.None);
            var snapshots = await register.GetKrrPersons(
                users.Select(u => u.Ssn).OfType<string>().Where(ssn => ssn.Length == 11),
                CancellationToken.None);
            persons = [.. snapshots.OrderBy(p => p.PersonIdentifier, StringComparer.Ordinal)];
        }
        catch (HttpRequestException ex)
        {
            // As the production client does for a failed call: no change log, the job tries again at its next run.
            logger.LogWarning(ex, "Localtest: the Register's KRR stand-in did not answer; KRR sync skipped");
            return null;
        }

        var hash = Hash(persons);
        lock (Gate)
        {
            if (_emitted is { } last && last.ChangeNumber == startingIdentifier && last.Hash == hash)
            {
                return new ContactRegisterChangesLog { ContactPreferencesSnapshots = [], StartingIdentifier = startingIdentifier };
            }

            _emitted = (startingIdentifier + 1, hash);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        logger.LogInformation("Localtest: KRR change {ChangeNumber} with {Count} test persons", startingIdentifier + 1, persons.Count);
        return new ContactRegisterChangesLog
        {
            StartingIdentifier = startingIdentifier,
            EndingIdentifier = startingIdentifier + 1,
            LatestChangeIdentifier = startingIdentifier + 1,
            ContactPreferencesSnapshots = [.. persons.Select(p => Stamp(p, now))],
        };
    }

    private static PersonContactPreferencesSnapshot Stamp(PersonContactPreferencesSnapshot person, DateTime now)
    {
        var email = Blank(person.ContactDetailsSnapshot?.Email);
        var mobile = Blank(person.ContactDetailsSnapshot?.MobileNumber);
        return new PersonContactPreferencesSnapshot
        {
            PersonIdentifier = person.PersonIdentifier,
            Reservation = person.Reservation,
            Status = person.Status,
            NotificationStatus = person.NotificationStatus,
            Language = person.Language,
            LanguageLastUpdated = person.LanguageLastUpdated,
            ContactDetailsSnapshot = new PersonContactDetailsSnapshot
            {
                Email = email,
                EmailLastUpdated = email is null ? null : now,
                MobileNumber = mobile,
                MobileNumberLastUpdated = mobile is null ? null : now,
            },
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string Hash(ImmutableList<PersonContactPreferencesSnapshot> persons)
    {
        var content = persons.Select(p => new[]
        {
            p.PersonIdentifier, p.Reservation, Blank(p.ContactDetailsSnapshot?.Email), Blank(p.ContactDetailsSnapshot?.MobileNumber), p.Language,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content))));
    }
}
