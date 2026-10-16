using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts;
using Altinn.Register.Core.A2;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Mediator;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.RateLimiting;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.Core.Utils;
using Microsoft.Extensions.Logging;

namespace Altinn.Register.Core.Operations;

#pragma warning disable SA1649 // File name should match first type name

/// <summary>
/// Request to get a v1 person based on request headers and the active user.
/// </summary>
/// <param name="NationalIdentityNumber">The national identity number to look up.</param>
/// <param name="LastName">The last name to match.</param>
/// <param name="ActiveUser">The active user's party UUID.</param>
public readonly record struct GetV1PersonRequest(
    PersonIdentifier NationalIdentityNumber,
    string LastName,
    Guid ActiveUser)
    : IRequest<Contracts.V1.Person>;

/// <summary>
/// Get v1 person from A2.
/// </summary>
internal sealed class GetV1PersonFromA2RequestHandler(IPersonLookup personLookup)
    : IRequestHandler<GetV1PersonRequest, Contracts.V1.Person>
{
    /// <inheritdoc/>
    public ValueTask<Result<Contracts.V1.Person>> Handle(GetV1PersonRequest request, CancellationToken cancellationToken)
        => personLookup.GetPerson(request.NationalIdentityNumber.ToString(), request.LastName, request.ActiveUser, cancellationToken);
}

/// <summary>
/// Get v1 person from the local data source.
/// </summary>
internal sealed partial class GetV1PersonFromDBRequestHandler(
    IUnitOfWorkManager manager,
    IRateLimiter rateLimiter,
    ILogger<GetV1PersonFromDBRequestHandler> logger)
    : IRequestHandler<GetV1PersonRequest, Contracts.V1.Person>
{
    private const string FailedAttemptsRateLimitPolicyName = "person-lookup-failed-attempts";

    /// <inheritdoc/>
    public async ValueTask<Result<Contracts.V1.Person>> Handle(GetV1PersonRequest request, CancellationToken cancellationToken)
    {
        if (await CheckUserFailedAttempts(request.ActiveUser, cancellationToken) is { IsProblem: true, Problem: var problem })
        {
            return problem;
        }

        await using var uow = await manager.CreateAsync(cancellationToken, activityName: "get v1 person");
        var persistence = uow.GetPartyPersistence();

        var person = await persistence
            .GetPersonByIdentifier(request.NationalIdentityNumber, PartyFieldIncludes.Party | PartyFieldIncludes.Person, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        if (person is not null
            && person.LastName.HasValue
            && person.LastName.Value is { Length: > 0 } lastNameFromParty
            && PersonNames.IsLastNamesSimilar(lastNameFromParty, request.LastName))
        {
            return V1PartyMapper.ToV1Person(person);
        }

        await IncrementFailedAttemptsByUser(request.ActiveUser, CancellationToken.None);
        return Problems.PersonNotFound;
    }

    private async ValueTask<Result> CheckUserFailedAttempts(Guid activeUser, CancellationToken cancellationToken)
    {
        var status = await rateLimiter.GetStatus(
            FailedAttemptsRateLimitPolicyName,
            activeUser.ToString("D"),
            cancellationToken);

        if (!status.IsBlocked)
        {
            return Result.Success;
        }

        Log.UserHasPerformedTooManyFailedPersonLookupAttempts(logger, activeUser);
        return Problems.PartyLookupTooManyFailedAttempts;
    }

    private async ValueTask IncrementFailedAttemptsByUser(Guid activeUser, CancellationToken cancellationToken)
    {
        await rateLimiter.Record(
            FailedAttemptsRateLimitPolicyName,
            activeUser.ToString("D"),
            cancellationToken: cancellationToken);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "User {UserPartyUuid} has performed too many failed person lookup attempts.")]
        public static partial void UserHasPerformedTooManyFailedPersonLookupAttempts(ILogger logger, Guid userPartyUuid);
    }
}
