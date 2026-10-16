#nullable enable

using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Clients.Interfaces;
using Altinn.Register.Contracts.V1;
using Altinn.Register.Core;
using Altinn.Register.Core.A2;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.RateLimiting;
using Altinn.Register.Core.Utils;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Services;

/// <summary>
/// Represents the business logic related to checking if a national identity number is in use.
/// </summary>
internal partial class PersonLookupService
    : IPersonLookup
{
    /// <summary>
    /// The rate-limit policy used to track failed person lookup attempts.
    /// </summary>
    public const string FailedAttemptsRateLimitPolicyName = "person-lookup-failed-attempts";

    private readonly IPersonClient _personsService;
    private readonly HybridCache _cache;
    private readonly IRateLimiter _rateLimiter;
    private readonly ILogger<PersonLookupService> _logger;
    private readonly HybridCacheEntryOptions _personCacheOptions;

    /// <summary>
    /// Initialize a new instance of the <see cref="PersonLookupService"/> class.
    /// </summary>
    public PersonLookupService(
        IPersonClient personsService,
        IOptions<PersonLookupSettings> personLookupSettings,
        HybridCache cache,
        IRateLimiter rateLimiter,
        ILogger<PersonLookupService> logger)
    {
        _personsService = personsService;
        _cache = cache;
        _rateLimiter = rateLimiter;
        _logger = logger;

        _personCacheOptions = new()
        {
            Expiration = TimeSpan.FromSeconds(personLookupSettings.Value.PersonCacheLifetimeSeconds),
        };
    }

    /// <inheritdoc/>
    public async ValueTask<Result<Person>> GetPerson(
        string nationalIdentityNumber,
        string lastName,
        Guid activeUser,
        CancellationToken cancellationToken = default)
    {
        if (await CheckUserFailedAttempts(activeUser, cancellationToken) is { IsProblem: true, Problem: var problem })
        {
            return problem;
        }

        string cacheKey = $"{nameof(PersonLookupService)}/{nameof(GetPerson)}/{nationalIdentityNumber}";

        var result = await _cache.GetOrCreateAsync(
            cacheKey,
            ct => FetchPerson(nationalIdentityNumber, ct),
            _personCacheOptions,
            cancellationToken: cancellationToken);

        if (!result.Found)
        {
            // we don't return here, because we want to treat a wrong id-number the same way as a wrong last name
            await _cache.RemoveAsync(cacheKey, cancellationToken);
        }

        var person = result.Person;
        if (person is { LastName: { Length: > 0 } lastNameFromParty }
            && PersonNames.IsLastNamesSimilar(lastNameFromParty, lastName))
        {
            return person;
        }

        // We do not pass the cancellation token here, such that a failed attempt is recorded even if the caller cancels the request while we are processing it.
        await IncrementFailedAttemptsByUser(activeUser, CancellationToken.None);
        return Problems.PersonNotFound;
    }

    private async ValueTask<CachedPersonResult> FetchPerson(
        string nationalIdentityNumber,
        CancellationToken cancellationToken)
    {
        Person? person = await _personsService.GetPerson(nationalIdentityNumber, cancellationToken);
        return new CachedPersonResult(person);
    }

    private async ValueTask<Result> CheckUserFailedAttempts(Guid activeUser, CancellationToken cancellationToken)
    {
        var status = await _rateLimiter.GetStatus(
            FailedAttemptsRateLimitPolicyName,
            activeUser.ToString("D"),
            cancellationToken);

        if (!status.IsBlocked)
        {
            return Result.Success;
        }

        Log.UserHasPerformedTooManyFailedPersonLookupAttempts(_logger, activeUser);
        return Problems.PartyLookupTooManyFailedAttempts;
    }

    private async ValueTask IncrementFailedAttemptsByUser(Guid activeUser, CancellationToken cancellationToken)
    {
        await _rateLimiter.Record(
            FailedAttemptsRateLimitPolicyName,
            activeUser.ToString("D"),
            cancellationToken: cancellationToken);
    }

    private readonly record struct CachedPersonResult(Person? Person)
    {
        [MemberNotNullWhen(true, nameof(Person))]
        public bool Found => Person is not null;
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "User {UserPartyUuid} has performed too many failed person lookup attempts.")]
        public static partial void UserHasPerformedTooManyFailedPersonLookupAttempts(ILogger logger, Guid userPartyUuid);
    }
}
