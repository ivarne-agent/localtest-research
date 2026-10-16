using System.Net;
using System.Net.Http.Json;
using System.Text;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Clients.Interfaces;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Operations;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.RateLimiting;
using Altinn.Register.Models;
using Altinn.Register.Services;
using Altinn.Register.TestUtils.TestData;
using Microsoft.Extensions.Configuration;

namespace Altinn.Register.IntegrationTests.Controllers;

public class PersonsControllerTests
    : IntegrationTestBase
{
    [Theory]
    [CombinatorialData]
    public async Task GetPerson_CorrectInput_OutcomeSuccessful(TestApiSource source)
    {
        const string FIRST_NAME = "fîrstnâme";
        const string LAST_NAME = "làstnâme";

        var (user, person) = await Setup(async (uow, ct) =>
        {
            var user = await uow.CreatePerson(cancellationToken: ct);
            var person = await uow.CreatePerson(name: PersonName.Create(FIRST_NAME, LAST_NAME), cancellationToken: ct);

            return (user, person);
        });

        SetSource(source);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/register/api/v1/persons/")
            .WithPersonToken(user)
            .WithPlatformToken("unittest")
            .WithHeader(PersonLookupIdentifiers.NationalIdentityNumberHeaderName, person.PersonIdentifier.Value!.ToString())
            .WithHeader(PersonLookupIdentifiers.LastNameHeaderName, Convert.ToBase64String(Encoding.UTF8.GetBytes("lastname")));

        if (source is TestApiSource.A2)
        {
            FakeHttpHandlers.For<IPersonClient>()
                .Expect(HttpMethod.Post, "/persons")
                .Respond(() => JsonContent.Create(V1PartyMapper.ToV1Person(person)));
        }

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPerson_MissingParameters_ReturnsBadRequest(TestApiSource source)
    {
        var (user, person) = await Setup(async (uow, ct) =>
        {
            var user = await uow.CreatePerson(cancellationToken: ct);
            var person = await uow.CreatePerson(cancellationToken: ct);

            return (user, person);
        });

        SetSource(source);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/register/api/v1/persons/")
            .WithPersonToken(user)
            .WithPlatformToken("unittest")
            .WithHeader(PersonLookupIdentifiers.NationalIdentityNumberHeaderName, person.PersonIdentifier.Value!.ToString());

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync(CancellationToken);
        content.ShouldContain(PersonLookupIdentifiers.LastNameHeaderName);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPerson_InvalidInput_ReturnsNotFound(TestApiSource source)
    {
        const string FIRST_NAME = "fîrstnâme";
        const string LAST_NAME = "låstnâme";

        var (user, person) = await Setup(async (uow, ct) =>
        {
            var user = await uow.CreatePerson(cancellationToken: ct);
            var person = await uow.CreatePerson(name: PersonName.Create(FIRST_NAME, LAST_NAME), cancellationToken: ct);

            return (user, person);
        });

        SetSource(source);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/register/api/v1/persons/")
            .WithPersonToken(user)
            .WithPlatformToken("unittest")
            .WithHeader(PersonLookupIdentifiers.NationalIdentityNumberHeaderName, person.PersonIdentifier.Value!.ToString())
            .WithHeader(PersonLookupIdentifiers.LastNameHeaderName, Convert.ToBase64String(Encoding.UTF8.GetBytes("lastname")));

        if (source is TestApiSource.A2)
        {
            FakeHttpHandlers.For<IPersonClient>()
                .Expect(HttpMethod.Post, "/persons")
                .Respond(() => JsonContent.Create(V1PartyMapper.ToV1Person(person)));
        }

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.NotFound);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPerson_TooManyAttempts_OutcomeTooManyRequests(TestApiSource source)
    {
        var (user, person) = await Setup(async (uow, ct) =>
        {
            var user = await uow.CreatePerson(cancellationToken: ct);
            var person = await uow.CreatePerson(cancellationToken: ct);

            return (user, person);
        });

        SetSource(source);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/register/api/v1/persons/")
            .WithPersonToken(user)
            .WithPlatformToken()
            .WithHeader(PersonLookupIdentifiers.NationalIdentityNumberHeaderName, person.PersonIdentifier.Value!.ToString())
            .WithHeader(PersonLookupIdentifiers.LastNameHeaderName, Convert.ToBase64String(Encoding.UTF8.GetBytes(person.LastName.Value!)));

        var userId = user.PartyUuid.Value.ToString("D");
        var rateLimiter = GetRequiredService<IRateLimiter>();
        await rateLimiter.Record(PersonLookupService.FailedAttemptsRateLimitPolicyName, userId, cost: 40, cancellationToken: CancellationToken);

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task GetPerson_CallAsOrg_OutcomeForbidden()
    {
        var person = await Setup(async (uow, ct) =>
        {
            var person = await uow.CreatePerson(cancellationToken: ct);

            return person;
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/register/api/v1/persons/")
            .WithOrganizationToken(OrganizationIdentifier.Parse("991825827"), orgCode: "ttd")
            .WithPlatformToken()
            .WithHeader(PersonLookupIdentifiers.NationalIdentityNumberHeaderName, person.PersonIdentifier.Value!.ToString())
            .WithHeader(PersonLookupIdentifiers.LastNameHeaderName, Convert.ToBase64String(Encoding.UTF8.GetBytes(person.LastName.Value!)));

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetPerson_AuthenticationLevelTooLow_ReturnsForbidden()
    {
        var user = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/register/api/v1/persons/")
            .WithPersonToken(user, authenticationLevel: 1)
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.Forbidden);
    }

    private void SetSource(TestApiSource source)
    {
        var sourceString = source switch
        {
            TestApiSource.A2 => "a2",
            TestApiSource.DB => "db",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };

        Configuration.AddInMemoryCollection([
            new("Altinn:register:ApiSource:Default", sourceString),
        ]);
    }
}
