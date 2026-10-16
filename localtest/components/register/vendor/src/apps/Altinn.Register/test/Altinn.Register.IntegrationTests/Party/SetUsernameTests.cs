using System.Net;
using System.Net.Http.Json;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.Models;
using Altinn.Register.TestUtils.TestData;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Altinn.Register.IntegrationTests.Party;

public class SetUsernameTests
    : IntegrationTestBase
{
    private const string EndpointUrl = "register/api/v2/internal/parties/set-username";

    [Fact]
    public async Task UsernameAlreadyInUseByAnotherParty_ReturnsConflict()
    {
        const string ExistingUsername = "existing-user";
        const string NewUsername = "new-user";

        var parties = await Setup(async (uow, ct) =>
        {
            var userIds = await uow.GetRequiredService<RegisterTestDataGenerator>().GetNextUserIds(2, ct);
            var owner = await uow.CreatePerson(user: new PartyUserRecord(userId: userIds[0], username: ExistingUsername), cancellationToken: ct);
            var target = await uow.CreatePerson(user: new PartyUserRecord(userId: userIds[1], username: NewUsername), cancellationToken: ct);

            return (owner, target);
        });

        using var response = await SetUsername(parties.target, ExistingUsername);

        await response.ShouldHaveStatusCode(HttpStatusCode.Conflict);
        await AssertProblem(response, Problems.UsernameInUse.ErrorCode);
        await AssertUsernames(parties.owner, current: ExistingUsername, values: [ExistingUsername]);
        await AssertUsernames(parties.target, current: NewUsername, values: [NewUsername]);
    }

    [Fact]
    public async Task UsernameAlreadyInUseBySameParty_ReturnsOkAndKeepsUsername()
    {
        const string Username = "same-user";

        var party = await Setup(async (uow, ct) =>
        {
            var userIds = await uow.GetRequiredService<RegisterTestDataGenerator>().GetNextUserIds(1, ct);
            return await uow.CreatePerson(user: new PartyUserRecord(userId: userIds[0], username: Username), cancellationToken: ct);
        });

        using var response = await SetUsername(party, Username);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        await AssertUsernames(party, current: Username, values: [Username]);
    }

    [Fact]
    public async Task PreviouslyUsedUsername_ReturnsOkAndMakesUsernameCurrentAgain()
    {
        const string PreviousUsername = "previous-user";
        const string CurrentUsername = "current-user";

        var party = await Setup(async (uow, ct) =>
        {
            var userIds = await uow.GetRequiredService<RegisterTestDataGenerator>().GetNextUserIds(1, ct);
            var person = await uow.CreatePerson(user: new PartyUserRecord(userId: userIds[0], username: PreviousUsername), cancellationToken: ct);

            var result = await uow.GetPartyPersistence().UpsertParty(
                person with
                {
                    Usernames = PartyHistoricalAggregate<string>.CreateCurrent(CurrentUsername),
                },
                ct);

            return result.ShouldHaveValue().ShouldBeOfType<PersonRecord>();
        });

        using var response = await SetUsername(party, PreviousUsername);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        await AssertUsernames(party, current: PreviousUsername, values: [PreviousUsername, CurrentUsername], versionIdChanged: true);
    }

    [Fact]
    public async Task PreviouslyUnusedUsername_ReturnsOkAndSetsUsername()
    {
        const string Username = "new-unused-user";

        var party = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));

        using var response = await SetUsername(party, Username);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        await AssertUsernames(party, current: Username, values: [Username], versionIdChanged: true);
    }

    [Fact]
    public async Task DeleteUsernameWhenPartyHasNoUsername_ReturnsOkAndKeepsNoUsername()
    {
        var party = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));

        using var response = await SetUsername(party, username: null);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        await AssertUsernames(party, current: null, values: []);
    }

    [Fact]
    public async Task DeleteUsernameWhenPartyHasUsername_ReturnsOkAndRemovesActiveUsername()
    {
        const string Username = "delete-user";

        var party = await Setup(async (uow, ct) =>
        {
            var userIds = await uow.GetRequiredService<RegisterTestDataGenerator>().GetNextUserIds(1, ct);
            return await uow.CreatePerson(user: new PartyUserRecord(userId: userIds[0], username: Username), cancellationToken: ct);
        });

        using var response = await SetUsername(party, username: null);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        await AssertUsernames(party, current: null, values: [Username], versionIdChanged: true);
    }

    [Fact]
    public async Task EmptyUsername_ReturnsBadRequest()
    {
        var party = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));

        using var response = await SetUsername(party, " ");

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, ValidationErrors.InvalidValue.ErrorCode, "/username");
    }

    [Fact]
    public async Task InvalidUsername_ReturnsBadRequest()
    {
        var party = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));

        using var response = await SetUsername(party, "1-invalid");

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, ValidationErrors.InvalidValue.ErrorCode, "/username");
    }

    [Fact]
    public async Task PartyDoesNotExist_ReturnsBadRequest()
    {
        var personIdentifier = await GetRequiredService<RegisterTestDataGenerator>()
            .GetNewPersonIdentifier(cancellationToken: TestContext.Current.CancellationToken);

        using var response = await SetUsername(personIdentifier, "missing-user");

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertProblem(response, Problems.ReferencedPartyNotFound.ErrorCode);
    }

    private async Task<HttpResponseMessage> SetUsername(PersonRecord party, string? username)
        => await SetUsername(party.PersonIdentifier.Value!, username);

    private async Task<HttpResponseMessage> SetUsername(PersonIdentifier personIdentifier, string? username)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
            .WithPlatformToken("unittest");

        request.Content = JsonContent.Create(
            new SetUsernameRequest
            {
                Party = PartyExternalRefUrn.PersonId.Create(personIdentifier).ToString(),
                Username = username,
            },
            options: JsonOptions);

        return await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task AssertUsernames(PersonRecord party, string? current, string[] values, bool versionIdChanged = false)
    {
        await Check(async (uow, ct) =>
        {
            var fromDb = await uow.GetPartyPersistence()
                .GetPartyById(party.PartyUuid.Value, PartyFieldIncludes.User | PartyFieldIncludes.PartyVersionId, ct)
                .FirstAsync(ct);

            fromDb.Usernames.CurrentValue.Value.ShouldBe(current);
            if (versionIdChanged)
            {
                fromDb.VersionId.Value.ShouldBeGreaterThan(party.VersionId.Value);
            }

            var connection = uow.GetRequiredService<NpgsqlConnection>();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = /*strpsql*/"SELECT username FROM register.username WHERE uuid = @partyId ORDER BY is_active DESC, username";
            cmd.Parameters.AddWithValue("partyId", party.PartyUuid.Value);

            var usernames = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                usernames.Add(reader.GetString(0));
            }

            usernames.ShouldBe(values);
        });
    }

    private static async Task AssertProblem(HttpResponseMessage response, ErrorCode expectedErrorCode)
    {
        var problem = await response.ShouldHaveJsonContent<AltinnProblemDetails>();
        problem.ErrorCode.ShouldBe(expectedErrorCode);
    }

    private static async Task AssertValidationError(HttpResponseMessage response, ErrorCode expectedErrorCode, string expectedPath)
    {
        var problem = await response.ShouldHaveJsonContent<AltinnValidationProblemDetails>();
        problem.ErrorCode.ShouldBe(StdProblemDescriptors.ErrorCodes.ValidationError);
        problem.Errors.ShouldContain(e => e.ErrorCode == expectedErrorCode && e.Paths.Contains(expectedPath));
    }
}
