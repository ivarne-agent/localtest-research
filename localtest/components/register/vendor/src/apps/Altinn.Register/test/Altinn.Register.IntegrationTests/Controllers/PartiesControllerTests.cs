using System.Net;
using System.Net.Http.Json;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Operations;
using Altinn.Register.Core.Parties;
using Altinn.Register.TestUtils.TestData;
using Microsoft.Extensions.Configuration;

namespace Altinn.Register.IntegrationTests.Controllers;

public class PartiesControllerTests
    : IntegrationTestBase
{
    [Theory]
    [CombinatorialData]
    public async Task GetParty_ValidTokenRequestForExistingParty_ReturnsParty(TestApiSource source)
    {
        var person = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Get, "/parties/{partyId}")
                .WithRouteValue("partyId", person.PartyId.Value.ToString())
                .Respond(() => JsonContent.Create(V1PartyMapper.ToV1Party(person)));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/{person.PartyId.Value}")
            .WithPersonToken(person)
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.Party>();

        actual.ShouldSatisfyAllConditions([
            (Contracts.V1.Party p) => p.PartyId.ShouldBe((int)person.PartyId.Value),
            (Contracts.V1.Party p) => p.PartyUuid.ShouldBe(person.PartyUuid.Value),
            (Contracts.V1.Party p) => p.PartyTypeName.ShouldBe(Contracts.V1.PartyType.Person),
            (Contracts.V1.Party p) => p.SSN.ShouldBe(person.PersonIdentifier.Value!.ToString()),
            (Contracts.V1.Party p) => p.Name.ShouldBe(person.ShortName.Value),
            (Contracts.V1.Party p) => p.Person.ShouldNotBeNull(),
            (Contracts.V1.Party p) => p.Organization.ShouldBeNull(),
        ]);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetParty_ValidTokenRequestForExistingOrganization_ReturnsParty(TestApiSource source)
    {
        var org = await Setup((uow, ct) => uow.CreateOrg(cancellationToken: ct));

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Get, "/parties/{partyId}")
                .WithRouteValue("partyId", org.PartyId.Value.ToString())
                .Respond(() => JsonContent.Create(V1PartyMapper.ToV1Party(org)));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/{org.PartyId.Value}")
            .WithOrganizationToken(org.OrganizationIdentifier.Value!, orgCode: "ttd")
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.Party>();

        actual.ShouldSatisfyAllConditions([
            (Contracts.V1.Party p) => p.PartyId.ShouldBe((int)org.PartyId.Value),
            (Contracts.V1.Party p) => p.PartyUuid.ShouldBe(org.PartyUuid.Value),
            (Contracts.V1.Party p) => p.PartyTypeName.ShouldBe(Contracts.V1.PartyType.Organisation),
            (Contracts.V1.Party p) => p.OrgNumber.ShouldBe(org.OrganizationIdentifier.Value!.ToString()),
            (Contracts.V1.Party p) => p.Name.ShouldBe(org.DisplayName.Value),
            (Contracts.V1.Party p) => p.UnitType.ShouldBe(org.UnitType.Value),
            (Contracts.V1.Party p) => p.Organization.ShouldNotBeNull(),
            (Contracts.V1.Party p) => p.Person.ShouldBeNull(),
        ]);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetParty_ValidTokenRequestForExistingSelfIdentifiedUser_ReturnsParty(TestApiSource source)
    {
        var siUser = await Setup((uow, ct) => uow.CreateSelfIdentifiedUser(cancellationToken: ct));
        var expectedName = V1PartyMapper.ToV1Party(siUser).Name;

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Get, "/parties/{partyId}")
                .WithRouteValue("partyId", siUser.PartyId.Value.ToString())
                .Respond(() => JsonContent.Create(V1PartyMapper.ToV1Party(siUser)));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/{siUser.PartyId.Value}")
            .WithOrganizationToken(OrganizationIdentifier.Parse("991825827"), orgCode: "ttd")
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.Party>();

        actual.ShouldSatisfyAllConditions([
            (Contracts.V1.Party p) => p.PartyId.ShouldBe((int)siUser.PartyId.Value),
            (Contracts.V1.Party p) => p.PartyUuid.ShouldBe(siUser.PartyUuid.Value),
            (Contracts.V1.Party p) => p.PartyTypeName.ShouldBe(Contracts.V1.PartyType.SelfIdentified),
            (Contracts.V1.Party p) => p.Name.ShouldBe(expectedName),
            (Contracts.V1.Party p) => p.Person.ShouldBeNull(),
            (Contracts.V1.Party p) => p.Organization.ShouldBeNull(),
        ]);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetParty_InvalidPartyId_ReturnsValidationProblem(
        TestApiSource source,
        [CombinatorialValues(0, -1)] int partyId)
    {
        SetSource(source);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/{partyId}")
            .WithOrganizationToken(OrganizationIdentifier.Parse("991825827"), orgCode: "ttd")
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, ValidationErrors.InvalidPartyId.ErrorCode);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetParty_ValidUserTokenRequestForNonExistingParty_ReturnsUnauthorized(TestApiSource source)
    {
        var (user, partyId) = await Setup(async (uow, ct) =>
        {
            var createdUser = await uow.CreatePerson(cancellationToken: ct);
            var nextPartyId = await uow.GetNextPartyId(ct);

            return (createdUser, (int)nextPartyId);
        });

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Get, "/parties/{partyId}")
                .WithRouteValue("partyId", partyId.ToString())
                .Respond(HttpStatusCode.NotFound);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/{partyId}")
            .WithPersonToken(user)
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetParty_ValidOrgTokenRequestForNonExistingParty_ReturnsNotFound(TestApiSource source)
    {
        var (org, partyId) = await Setup(async (uow, ct) =>
        {
            var createdOrg = await uow.CreateOrg(cancellationToken: ct);
            var nextPartyId = await uow.GetNextPartyId(ct);

            return (createdOrg, (int)nextPartyId);
        });

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Get, "/parties/{partyId}")
                .WithRouteValue("partyId", partyId.ToString())
                .Respond(HttpStatusCode.NotFound);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/{partyId}")
            .WithOrganizationToken(org.OrganizationIdentifier.Value!, orgCode: "ttd")
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.NotFound);
        var content = await response.ShouldHaveJsonContent<AltinnProblemDetails>();
        content.ErrorCode.ShouldBe(Problems.PartyNotFound.ErrorCode);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetParty_InvalidToken_ReturnsUnauthorized(TestApiSource source)
    {
        var partyId = await Setup(async (uow, ct) => (int)await uow.GetNextPartyId(ct));
        SetSource(source);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/{partyId}")
            .WithPlatformToken();
        request.Headers.Authorization = new("Bearer", "bogus");

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetParty_MissingAccessToken_ReturnsForbidden(TestApiSource source)
    {
        var person = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));
        SetSource(source);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/{person.PartyId.Value}")
            .WithPersonToken(person);

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetParty_EndpointSourceOverride_UsesEndpointSource()
    {
        var person = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));

        SetSource(TestApiSource.A2);
        SetSourceForEndpoint("parties/get/by-id", TestApiSource.DB);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/{person.PartyId.Value}")
            .WithPersonToken(person)
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.Party>();
        actual.PartyId.ShouldBe((int)person.PartyId.Value);
        actual.PartyUuid.ShouldBe(person.PartyUuid.Value);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyByUuid_ValidTokenRequestForExistingParty_ReturnsParty(TestApiSource source)
    {
        var person = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Get, "/parties")
                .WithQuery("partyuuid", person.PartyUuid.Value.ToString())
                .Respond(() => JsonContent.Create(V1PartyMapper.ToV1Party(person)));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/byuuid/{person.PartyUuid.Value}")
            .WithPersonToken(person)
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.Party>();

        actual.ShouldSatisfyAllConditions([
            (Contracts.V1.Party p) => p.PartyId.ShouldBe((int)person.PartyId.Value),
            (Contracts.V1.Party p) => p.PartyUuid.ShouldBe(person.PartyUuid.Value),
            (Contracts.V1.Party p) => p.PartyTypeName.ShouldBe(Contracts.V1.PartyType.Person),
            (Contracts.V1.Party p) => p.SSN.ShouldBe(person.PersonIdentifier.Value!.ToString()),
            (Contracts.V1.Party p) => p.Name.ShouldBe(person.ShortName.Value),
            (Contracts.V1.Party p) => p.Person.ShouldNotBeNull(),
            (Contracts.V1.Party p) => p.Organization.ShouldBeNull(),
        ]);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyByUuid_ValidTokenRequestForExistingOrganization_ReturnsParty(TestApiSource source)
    {
        var org = await Setup((uow, ct) => uow.CreateOrg(cancellationToken: ct));

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Get, "/parties")
                .WithQuery("partyuuid", org.PartyUuid.Value.ToString())
                .Respond(() => JsonContent.Create(V1PartyMapper.ToV1Party(org)));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/byuuid/{org.PartyUuid.Value}")
            .WithOrganizationToken(org.OrganizationIdentifier.Value!, orgCode: "ttd")
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.Party>();

        actual.ShouldSatisfyAllConditions([
            (Contracts.V1.Party p) => p.PartyId.ShouldBe((int)org.PartyId.Value),
            (Contracts.V1.Party p) => p.PartyUuid.ShouldBe(org.PartyUuid.Value),
            (Contracts.V1.Party p) => p.PartyTypeName.ShouldBe(Contracts.V1.PartyType.Organisation),
            (Contracts.V1.Party p) => p.OrgNumber.ShouldBe(org.OrganizationIdentifier.Value!.ToString()),
            (Contracts.V1.Party p) => p.Name.ShouldBe(org.DisplayName.Value),
            (Contracts.V1.Party p) => p.UnitType.ShouldBe(org.UnitType.Value),
            (Contracts.V1.Party p) => p.Organization.ShouldNotBeNull(),
            (Contracts.V1.Party p) => p.Person.ShouldBeNull(),
        ]);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyByUuid_ValidTokenRequestForExistingSelfIdentifiedUser_ReturnsParty(TestApiSource source)
    {
        const string email = "idporten-email-user@example.com";
        var siUser = await Setup((uow, ct) => uow.CreateSelfIdentifiedUser(
            type: SelfIdentifiedUserType.IdPortenEmail,
            email: email,
            cancellationToken: ct));

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Get, "/parties")
                .WithQuery("partyuuid", siUser.PartyUuid.Value.ToString())
                .Respond(() => JsonContent.Create(V1PartyMapper.ToV1Party(siUser)));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/byuuid/{siUser.PartyUuid.Value}")
            .WithOrganizationToken(OrganizationIdentifier.Parse("991825827"), orgCode: "ttd")
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.Party>();

        actual.ShouldSatisfyAllConditions([
            (Contracts.V1.Party p) => p.PartyId.ShouldBe((int)siUser.PartyId.Value),
            (Contracts.V1.Party p) => p.PartyUuid.ShouldBe(siUser.PartyUuid.Value),
            (Contracts.V1.Party p) => p.PartyTypeName.ShouldBe(Contracts.V1.PartyType.SelfIdentified),
            (Contracts.V1.Party p) => p.Name.ShouldBe($"epost:{email}"),
            (Contracts.V1.Party p) => p.Person.ShouldBeNull(),
            (Contracts.V1.Party p) => p.Organization.ShouldBeNull(),
        ]);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyByUuid_ValidUserTokenRequestForNonExistingParty_ReturnsUnauthorized(TestApiSource source)
    {
        var (user, partyUuid) = await Setup(async (uow, ct) =>
        {
            var createdUser = await uow.CreatePerson(cancellationToken: ct);
            var nextPartyUuid = Guid.CreateVersion7();

            return (createdUser, nextPartyUuid);
        });

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Get, "/parties")
                .WithQuery("partyuuid", partyUuid.ToString())
                .Respond(HttpStatusCode.NotFound);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/byuuid/{partyUuid}")
            .WithPersonToken(user)
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyByUuid_ValidOrgTokenRequestForNonExistingParty_ReturnsNotFound(TestApiSource source)
    {
        var (org, partyUuid) = await Setup(async (uow, ct) =>
        {
            var createdOrg = await uow.CreateOrg(cancellationToken: ct);
            var nextPartyUuid = Guid.CreateVersion7();

            return (createdOrg, nextPartyUuid);
        });

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Get, "/parties")
                .WithQuery("partyuuid", partyUuid.ToString())
                .Respond(HttpStatusCode.NotFound);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/byuuid/{partyUuid}")
            .WithOrganizationToken(org.OrganizationIdentifier.Value!, orgCode: "ttd")
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.NotFound);
        var content = await response.ShouldHaveJsonContent<AltinnProblemDetails>();
        content.ErrorCode.ShouldBe(Problems.PartyNotFound.ErrorCode);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyByUuid_InvalidToken_ReturnsUnauthorized(TestApiSource source)
    {
        var partyUuid = await Setup(async (uow, ct) =>
        {
            var person = await uow.CreatePerson(cancellationToken: ct);
            return person.PartyUuid.Value;
        });

        SetSource(source);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/byuuid/{partyUuid}")
            .WithPlatformToken();
        request.Headers.Authorization = new("Bearer", "bogus");

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyByUuid_MissingAccessToken_ReturnsForbidden(TestApiSource source)
    {
        var person = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));
        SetSource(source);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/byuuid/{person.PartyUuid.Value}")
            .WithPersonToken(person);

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetPartyByUuid_EndpointSourceOverride_UsesEndpointSource()
    {
        var person = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));

        SetSource(TestApiSource.A2);
        SetSourceForEndpoint("parties/get/by-uuid", TestApiSource.DB);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"register/api/v1/parties/byuuid/{person.PartyUuid.Value}")
            .WithPersonToken(person)
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.Party>();
        actual.PartyId.ShouldBe((int)person.PartyId.Value);
        actual.PartyUuid.ShouldBe(person.PartyUuid.Value);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyListById_ReturnsPartyList(TestApiSource source)
    {
        var (person1, person2) = await Setup(async (uow, ct) =>
        {
            var first = await uow.CreatePerson(cancellationToken: ct);
            var second = await uow.CreatePerson(cancellationToken: ct);
            return (first, second);
        });

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Post, "/parties")
                .WithQuery("fetchSubUnits", "False")
                .Respond(() => JsonContent.Create(new[]
                {
                    V1PartyMapper.ToV1Party(person1),
                    V1PartyMapper.ToV1Party(person2),
                }));
        }

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/partylist",
            new[] { (int)person1.PartyId.Value, (int)person2.PartyId.Value },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<List<Contracts.V1.Party>>();

        actual.Count.ShouldBe(2);
        actual.Select(static party => party.PartyId)
            .ShouldBe(
                [(int)person1.PartyId.Value, (int)person2.PartyId.Value],
                ignoreOrder: true);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyListById_FetchSubUnits_ReturnsPartyListWithSubUnits(TestApiSource source)
    {
        var (parent1, parent1Children, parent2, parent2Children) = await Setup(async (uow, ct) =>
        {
            var parent1 = await uow.CreateOrg(unitType: "AS", cancellationToken: ct);
            var parent2 = await uow.CreateOrg(unitType: "AS", cancellationToken: ct);

            var parent1Children = await uow.CreateOrgs(count: 3, unitType: "BEDR", cancellationToken: ct);
            var parent2Children = await uow.CreateOrgs(count: 5, unitType: "BEDR", cancellationToken: ct);

            foreach (var child in parent1Children)
            {
                await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: child.PartyUuid.Value, to: parent1.PartyUuid.Value, cancellationToken: ct);
            }

            foreach (var child in parent2Children)
            {
                await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: child.PartyUuid.Value, to: parent2.PartyUuid.Value, cancellationToken: ct);
            }

            return (parent1, parent1Children, parent2, parent2Children);
        });

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Post, "/parties")
                .WithQuery("fetchSubUnits", "True")
                .Respond(() => JsonContent.Create(new[]
                {
                    V1PartyMapper.ToV1Party(parent1, parent1Children),
                    V1PartyMapper.ToV1Party(parent2, parent2Children),
                }));
        }

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/partylist?fetchSubUnits=true",
            new[] { (int)parent1.PartyId.Value, (int)parent2.PartyId.Value },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<List<Contracts.V1.Party>>();

        actual.Count.ShouldBe(2);
        actual.Select(static party => party.PartyId).ShouldBe([(int)parent1.PartyId.Value, (int)parent2.PartyId.Value], ignoreOrder: true);

        var parent1Actual = actual.Find(party => party.PartyId == (int)parent1.PartyId.Value);
        var parent2Actual = actual.Find(party => party.PartyId == (int)parent2.PartyId.Value);

        parent1Actual.ShouldNotBeNull();
        parent2Actual.ShouldNotBeNull();

        parent1Actual!.ChildParties.ShouldNotBeNull();
        parent2Actual!.ChildParties.ShouldNotBeNull();

        parent1Actual.ChildParties!.Select(static party => party.PartyId)
            .ShouldBe(parent1Children.Select(child => (int)child.PartyId.Value), ignoreOrder: true);

        parent2Actual.ChildParties!.Select(static party => party.PartyId)
            .ShouldBe(parent2Children.Select(child => (int)child.PartyId.Value), ignoreOrder: true);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyListById_NoMatches_ReturnsNotFound(TestApiSource source)
    {
        var partyId = await Setup(async (uow, ct) => (int)await uow.GetNextPartyId(ct));

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Post, "/parties")
                .WithQuery("fetchSubUnits", "False")
                .Respond(() => JsonContent.Create(Array.Empty<Contracts.V1.Party>()));
        }

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/partylist",
            new[] { partyId },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPartyListById_EndpointSourceOverride_UsesEndpointSource()
    {
        var person = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));

        SetSource(TestApiSource.A2);
        SetSourceForEndpoint("parties/partylist/by-id", TestApiSource.DB);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/partylist",
            new[] { (int)person.PartyId.Value },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<List<Contracts.V1.Party>>();
        actual.Select(static party => party.PartyId).ShouldBe([(int)person.PartyId.Value]);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyListByUuid_ReturnsPartyList(TestApiSource source)
    {
        var (person1, person2) = await Setup(async (uow, ct) =>
        {
            var first = await uow.CreatePerson(cancellationToken: ct);
            var second = await uow.CreatePerson(cancellationToken: ct);
            return (first, second);
        });

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Post, "/parties/byuuid")
                .WithQuery("fetchSubUnits", "False")
                .Respond(() => JsonContent.Create(new[]
                {
                    V1PartyMapper.ToV1Party(person1),
                    V1PartyMapper.ToV1Party(person2),
                }));
        }

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/partylistbyuuid",
            new[] { person1.PartyUuid.Value, person2.PartyUuid.Value },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<List<Contracts.V1.Party>>();

        actual.Count.ShouldBe(2);
        actual.Select(static party => party.PartyUuid)
            .ShouldBe(
                [person1.PartyUuid.Value, person2.PartyUuid.Value],
                ignoreOrder: true);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyListByUuid_FetchSubUnits_ReturnsPartyListWithSubUnits(TestApiSource source)
    {
        var (parent1, parent1Children, parent2, parent2Children) = await Setup(async (uow, ct) =>
        {
            var parent1 = await uow.CreateOrg(unitType: "AS", cancellationToken: ct);
            var parent2 = await uow.CreateOrg(unitType: "AS", cancellationToken: ct);

            var parent1Children = await uow.CreateOrgs(count: 3, unitType: "BEDR", cancellationToken: ct);
            var parent2Children = await uow.CreateOrgs(count: 5, unitType: "BEDR", cancellationToken: ct);

            foreach (var child in parent1Children)
            {
                await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: child.PartyUuid.Value, to: parent1.PartyUuid.Value, cancellationToken: ct);
            }

            foreach (var child in parent2Children)
            {
                await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: child.PartyUuid.Value, to: parent2.PartyUuid.Value, cancellationToken: ct);
            }

            return (parent1, parent1Children, parent2, parent2Children);
        });

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Post, "/parties/byuuid")
                .WithQuery("fetchSubUnits", "True")
                .Respond(() => JsonContent.Create(new[]
                {
                    V1PartyMapper.ToV1Party(parent1, parent1Children),
                    V1PartyMapper.ToV1Party(parent2, parent2Children),
                }));
        }

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/partylistbyuuid?fetchSubUnits=true",
            new[] { parent1.PartyUuid.Value, parent2.PartyUuid.Value },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<List<Contracts.V1.Party>>();

        actual.Count.ShouldBe(2);
        actual.Select(static party => party.PartyUuid).ShouldBe([parent1.PartyUuid.Value, parent2.PartyUuid.Value], ignoreOrder: true);

        var parent1Actual = actual.Find(party => party.PartyUuid == parent1.PartyUuid.Value);
        var parent2Actual = actual.Find(party => party.PartyUuid == parent2.PartyUuid.Value);

        parent1Actual.ShouldNotBeNull();
        parent2Actual.ShouldNotBeNull();

        parent1Actual!.ChildParties.ShouldNotBeNull();
        parent2Actual!.ChildParties.ShouldNotBeNull();

        parent1Actual.ChildParties!.Select(static party => party.PartyUuid)
            .ShouldBe(parent1Children.Select(child => child.PartyUuid.Value), ignoreOrder: true);

        parent2Actual.ChildParties!.Select(static party => party.PartyUuid)
            .ShouldBe(parent2Children.Select(child => child.PartyUuid.Value), ignoreOrder: true);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyListByUuid_NoMatches_ReturnsEmptyList(TestApiSource source)
    {
        var partyUuid = Guid.NewGuid();

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Post, "/parties/byuuid")
                .WithQuery("fetchSubUnits", "False")
                .Respond(() => JsonContent.Create(Array.Empty<Contracts.V1.Party>()));
        }

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/partylistbyuuid",
            new[] { partyUuid },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<List<Contracts.V1.Party>>();
        actual.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPartyListByUuid_EndpointSourceOverride_UsesEndpointSource()
    {
        var person = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));

        SetSource(TestApiSource.A2);
        SetSourceForEndpoint("parties/partylist/by-uuid", TestApiSource.DB);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/partylistbyuuid",
            new[] { person.PartyUuid.Value },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<List<Contracts.V1.Party>>();
        actual.Select(static party => party.PartyUuid).ShouldBe([person.PartyUuid.Value]);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyLookup_ValidTokenRequestForExistingOrganization_ReturnsParty(TestApiSource source)
    {
        var org = await Setup((uow, ct) => uow.CreateOrg(cancellationToken: ct));
        var orgNo = org.OrganizationIdentifier.Value!.ToString();

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Post, "/parties/lookupObject")
                .Respond(() => JsonContent.Create(V1PartyMapper.ToV1Party(org)));
        }

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/lookup",
            new Contracts.V1.PartyLookup { OrgNo = orgNo },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.Party>();

        actual.ShouldSatisfyAllConditions([
            (Contracts.V1.Party p) => p.PartyId.ShouldBe((int)org.PartyId.Value),
            (Contracts.V1.Party p) => p.PartyUuid.ShouldBe(org.PartyUuid.Value),
            (Contracts.V1.Party p) => p.PartyTypeName.ShouldBe(Contracts.V1.PartyType.Organisation),
            (Contracts.V1.Party p) => p.OrgNumber.ShouldBe(orgNo),
            (Contracts.V1.Party p) => p.Name.ShouldBe(org.DisplayName.Value),
            (Contracts.V1.Party p) => p.UnitType.ShouldBe(org.UnitType.Value),
            (Contracts.V1.Party p) => p.Organization.ShouldNotBeNull(),
            (Contracts.V1.Party p) => p.Person.ShouldBeNull(),
        ]);

        actual.Organization!.ShouldSatisfyAllConditions([
            (Contracts.V1.Organization o) => o.OrgNumber.ShouldBe(orgNo),
            (Contracts.V1.Organization o) => o.Name.ShouldBe(org.DisplayName.Value),
            (Contracts.V1.Organization o) => o.UnitType.ShouldBe(org.UnitType.Value),
        ]);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyLookup_ValidTokenRequestForExistingPerson_ReturnsParty(TestApiSource source)
    {
        var person = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));
        var ssn = person.PersonIdentifier.Value!.ToString();

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Post, "/parties/lookupObject")
                .Respond(() => JsonContent.Create(V1PartyMapper.ToV1Party(person)));
        }

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/lookup",
            new Contracts.V1.PartyLookup { Ssn = ssn },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.Party>();

        actual.ShouldSatisfyAllConditions([
            (Contracts.V1.Party p) => p.PartyId.ShouldBe((int)person.PartyId.Value),
            (Contracts.V1.Party p) => p.PartyUuid.ShouldBe(person.PartyUuid.Value),
            (Contracts.V1.Party p) => p.PartyTypeName.ShouldBe(Contracts.V1.PartyType.Person),
            (Contracts.V1.Party p) => p.SSN.ShouldBe(ssn),
            (Contracts.V1.Party p) => p.Name.ShouldBe(person.ShortName.Value),
            (Contracts.V1.Party p) => p.Person.ShouldNotBeNull(),
            (Contracts.V1.Party p) => p.Organization.ShouldBeNull(),
        ]);

        actual.Person!.ShouldSatisfyAllConditions([
            (Contracts.V1.Person p) => p.SSN.ShouldBe(ssn),
            (Contracts.V1.Person p) => p.Name.ShouldBe(person.ShortName.Value),
            (Contracts.V1.Person p) => p.FirstName.ShouldBe(person.FirstName.Value),
            (Contracts.V1.Person p) => p.MiddleName.ShouldBe(person.MiddleName.Value),
            (Contracts.V1.Person p) => p.LastName.ShouldBe(person.LastName.Value),
        ]);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyLookup_ValidTokenRequestForNonExistingOrganization_ReturnsNotFound(TestApiSource source)
    {
        var orgNo = await GetRequiredService<RegisterTestDataGenerator>().GetNewOrgNumber(CancellationToken);

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Post, "/parties/lookupObject")
                .Respond(HttpStatusCode.NotFound);
        }

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/lookup",
            new Contracts.V1.PartyLookup { OrgNo = orgNo.ToString() },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.NotFound);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyLookup_InvalidToken_ReturnsUnauthorized(TestApiSource source)
    {
        var orgNo = await GetRequiredService<RegisterTestDataGenerator>().GetNewOrgNumber(CancellationToken);
        SetSource(source);

        var request = new HttpRequestMessage(HttpMethod.Post, "register/api/v1/parties/lookup")
        {
            Content = JsonContent.Create(new Contracts.V1.PartyLookup { OrgNo = orgNo.ToString() }),
        };
        request.Headers.Authorization = new("Bearer", "bogus");

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyLookup_InvalidOrgNo_ReturnsValidationProblem(TestApiSource source)
    {
        var validOrgNo = (await Setup((uow, ct) => uow.CreateOrg(cancellationToken: ct))).OrganizationIdentifier.Value!.ToString();
        var invalidOrgNo = ChangeLastDigit(validOrgNo);

        SetSource(source);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/lookup",
            new Contracts.V1.PartyLookup { OrgNo = invalidOrgNo },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, ValidationErrors.InvalidOrganizationNumber.ErrorCode);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyLookup_InvalidSsn_ReturnsValidationProblem(TestApiSource source)
    {
        var validSsn = (await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct))).PersonIdentifier.Value!.ToString();
        var invalidSsn = ChangeLastDigit(validSsn);

        SetSource(source);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/lookup",
            new Contracts.V1.PartyLookup { Ssn = invalidSsn },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, ValidationErrors.InvalidPersonNumber.ErrorCode);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyLookup_MissingIdentifiers_ReturnsValidationProblem(TestApiSource source)
    {
        SetSource(source);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/lookup",
            new Contracts.V1.PartyLookup(),
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, StdValidationErrors.Required.ErrorCode);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyLookup_BothIdentifiersProvided_ReturnsValidationProblem(TestApiSource source)
    {
        var (person, org) = await Setup(async (uow, ct) =>
        {
            var createdPerson = await uow.CreatePerson(cancellationToken: ct);
            var createdOrg = await uow.CreateOrg(cancellationToken: ct);

            return (createdPerson, createdOrg);
        });

        SetSource(source);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/lookup",
            new Contracts.V1.PartyLookup
            {
                Ssn = person.PersonIdentifier.Value!.ToString(),
                OrgNo = org.OrganizationIdentifier.Value!.ToString(),
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, ValidationErrors.MutuallyExclusive.ErrorCode);
    }

    [Fact]
    public async Task PostPartyLookup_EndpointSourceOverride_UsesEndpointSource()
    {
        var org = await Setup((uow, ct) => uow.CreateOrg(cancellationToken: ct));
        var orgNo = org.OrganizationIdentifier.Value!.ToString();

        SetSource(TestApiSource.A2);
        SetSourceForEndpoint("parties/lookup", TestApiSource.DB);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/lookup",
            new Contracts.V1.PartyLookup { OrgNo = orgNo },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.Party>();
        actual.ShouldSatisfyAllConditions([
            (Contracts.V1.Party p) => p.PartyTypeName.ShouldBe(Contracts.V1.PartyType.Organisation),
            (Contracts.V1.Party p) => p.OrgNumber.ShouldBe(orgNo),
            (Contracts.V1.Party p) => p.Organization.ShouldNotBeNull(),
        ]);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyNamesLookup_ValidTokenRequestForExistingOrganization_ReturnsPartyName(TestApiSource source)
    {
        var org = await Setup((uow, ct) => uow.CreateOrg(cancellationToken: ct));
        var orgNo = org.OrganizationIdentifier.Value!.ToString();

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Post, "/parties/lookupObject")
                .Respond(() => JsonContent.Create(V1PartyMapper.ToV1Party(org)));
        }

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/nameslookup",
            new Contracts.V1.PartyNamesLookup
            {
                Parties = [new() { OrgNo = orgNo }],
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.PartyNamesLookupResult>();
        actual.PartyNames.ShouldBe([
            new Contracts.V1.PartyName
            {
                OrgNo = orgNo,
                Name = org.DisplayName.Value,
            }
        ]);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyNamesLookup_QueryParameterHandling_ReturnsExpectedPersonName(
        TestApiSource source,
        [CombinatorialMemberData(nameof(PartyNamesLookupQueryParameterHandlingData))]
        PartyNamesLookupQueryParameterHandling queryParameters)
    {
        var person = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));
        var ssn = person.PersonIdentifier.Value!.ToString();

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            FakeHttpHandlers.For<IV1PartyService>()
                .Expect(HttpMethod.Post, "/parties/lookupObject")
                .Respond(() => JsonContent.Create(V1PartyMapper.ToV1Party(person)));
        }

        var response = await HttpClient.PostAsJsonAsync(
            $"register/api/v1/parties/nameslookup{queryParameters.QueryString}",
            new Contracts.V1.PartyNamesLookup
            {
                Parties = [new() { Ssn = ssn }],
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.PartyNamesLookupResult>();
        actual.PartyNames.ShouldBe([
            new Contracts.V1.PartyName
            {
                Ssn = ssn,
                Name = person.ShortName.Value,
                PersonName = queryParameters.IncludePersonName
                    ? new Contracts.V1.PersonNameComponents
                    {
                        FirstName = person.FirstName.Value,
                        MiddleName = person.MiddleName.Value,
                        LastName = person.LastName.Value,
                    }
                    : null,
            }
        ]);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyNamesLookup_ValidTokenRequestForExistingOrganizations_ReturnsPartyNames(TestApiSource source)
    {
        var (org1, org2) = await Setup(async (uow, ct) =>
        {
            var createdOrg1 = await uow.CreateOrg(cancellationToken: ct);
            var createdOrg2 = await uow.CreateOrg(cancellationToken: ct);

            return (createdOrg1, createdOrg2);
        });
        var orgNumbers = new[] { org1.OrganizationIdentifier.Value!.ToString(), org2.OrganizationIdentifier.Value!.ToString() };

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            ConfigureA2PartyLookupResponses([
                V1PartyMapper.ToV1Party(org1),
                V1PartyMapper.ToV1Party(org2),
            ]);
        }

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/nameslookup",
            new Contracts.V1.PartyNamesLookup
            {
                Parties =
                [
                    new() { OrgNo = orgNumbers[0] },
                    new() { OrgNo = orgNumbers[1] },
                ],
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.PartyNamesLookupResult>();
        var expected = new[]
        {
            new Contracts.V1.PartyName
            {
                OrgNo = orgNumbers[0],
                Name = org1.DisplayName.Value,
            },
            new Contracts.V1.PartyName
            {
                OrgNo = orgNumbers[1],
                Name = org2.DisplayName.Value,
            },
        };

        actual.PartyNames.ShouldNotBeNull();
        actual.PartyNames!
            .ShouldBe(expected, ignoreOrder: true);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyNamesLookup_PartialUnknownInput_ReturnsNullNameForUnknownParty(TestApiSource source)
    {
        var org = await Setup((uow, ct) => uow.CreateOrg(cancellationToken: ct));
        var validOrgNo = org.OrganizationIdentifier.Value!.ToString();
        var unknownOrgNo = (await GetRequiredService<RegisterTestDataGenerator>().GetNewOrgNumber(CancellationToken)).ToString();

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            ConfigureA2PartyLookupResponses([
                V1PartyMapper.ToV1Party(org),
            ]);
        }

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/nameslookup",
            new Contracts.V1.PartyNamesLookup
            {
                Parties =
                [
                    new() { OrgNo = unknownOrgNo },
                    new() { OrgNo = validOrgNo },
                ],
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.PartyNamesLookupResult>();
        var expected = new[]
        {
            new Contracts.V1.PartyName
            {
                OrgNo = unknownOrgNo,
                Name = null,
            },
            new Contracts.V1.PartyName
            {
                OrgNo = validOrgNo,
                Name = org.DisplayName.Value,
            },
        };

        actual.PartyNames.ShouldNotBeNull();
        actual.PartyNames!
            .ShouldBe(expected, ignoreOrder: true);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyNamesLookup_UnknownAndDuplicateInputs_PreservesDuplicatesAndUnknowns(TestApiSource source)
    {
        var org = await Setup((uow, ct) => uow.CreateOrg(cancellationToken: ct));
        var validOrgNo = org.OrganizationIdentifier.Value!.ToString();
        var unknownOrgNo = (await GetRequiredService<RegisterTestDataGenerator>().GetNewOrgNumber(CancellationToken)).ToString();

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            ConfigureA2PartyLookupResponses([
                V1PartyMapper.ToV1Party(org),
            ]);
        }

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/nameslookup",
            new Contracts.V1.PartyNamesLookup
            {
                Parties =
                [
                    new() { OrgNo = unknownOrgNo },
                    new() { OrgNo = validOrgNo },
                    new() { OrgNo = validOrgNo },
                ],
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.PartyNamesLookupResult>();
        var expected = new[]
        {
            new Contracts.V1.PartyName
            {
                OrgNo = unknownOrgNo,
                Name = null,
            },
            new Contracts.V1.PartyName
            {
                OrgNo = validOrgNo,
                Name = org.DisplayName.Value,
            },
            new Contracts.V1.PartyName
            {
                OrgNo = validOrgNo,
                Name = org.DisplayName.Value,
            },
        };

        actual.PartyNames.ShouldNotBeNull();
        actual.PartyNames!
            .ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public async Task PostPartyNamesLookup_EndpointSourceOverride_UsesEndpointSource()
    {
        var org = await Setup((uow, ct) => uow.CreateOrg(cancellationToken: ct));
        var orgNo = org.OrganizationIdentifier.Value!.ToString();

        SetSource(TestApiSource.A2);
        SetSourceForEndpoint("parties/nameslookup", TestApiSource.DB);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/nameslookup",
            new Contracts.V1.PartyNamesLookup
            {
                Parties = [new() { OrgNo = orgNo }],
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.ShouldHaveJsonContent<Contracts.V1.PartyNamesLookupResult>();
        actual.PartyNames.ShouldBe([
            new Contracts.V1.PartyName
            {
                OrgNo = orgNo,
                Name = org.DisplayName.Value,
            }
        ]);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyNamesLookup_InvalidQueryParameter_ReturnsBadRequest(
        TestApiSource source,
        [CombinatorialMemberData(nameof(PartyNamesLookupInvalidQueryParameters))] string queryString)
    {
        var person = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));
        var ssn = person.PersonIdentifier.Value!.ToString();

        SetSource(source);

        var response = await HttpClient.PostAsJsonAsync(
            $"register/api/v1/parties/nameslookup{queryString}",
            new Contracts.V1.PartyNamesLookup
            {
                Parties = [new Contracts.V1.PartyLookup { Ssn = ssn }],
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyNamesLookup_MissingIdentifiers_ReturnsValidationProblem(TestApiSource source)
    {
        SetSource(source);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/nameslookup",
            new Contracts.V1.PartyNamesLookup
            {
                Parties = [new Contracts.V1.PartyLookup()],
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, StdValidationErrors.Required.ErrorCode);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyNamesLookup_BothIdentifiersProvided_ReturnsValidationProblem(TestApiSource source)
    {
        var (person, org) = await Setup(async (uow, ct) =>
        {
            var createdPerson = await uow.CreatePerson(cancellationToken: ct);
            var createdOrg = await uow.CreateOrg(cancellationToken: ct);

            return (createdPerson, createdOrg);
        });

        SetSource(source);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/nameslookup",
            new Contracts.V1.PartyNamesLookup
            {
                Parties =
                [
                    new Contracts.V1.PartyLookup
                    {
                        Ssn = person.PersonIdentifier.Value!.ToString(),
                        OrgNo = org.OrganizationIdentifier.Value!.ToString(),
                    }
                ],
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, ValidationErrors.MutuallyExclusive.ErrorCode);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyNamesLookup_InvalidOrgNo_ReturnsValidationProblem(TestApiSource source)
    {
        var validOrgNo = (await Setup((uow, ct) => uow.CreateOrg(cancellationToken: ct))).OrganizationIdentifier.Value!.ToString();
        var invalidOrgNo = ChangeLastDigit(validOrgNo);

        SetSource(source);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/nameslookup",
            new Contracts.V1.PartyNamesLookup
            {
                Parties = [new Contracts.V1.PartyLookup { OrgNo = invalidOrgNo }],
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, ValidationErrors.InvalidOrganizationNumber.ErrorCode);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyNamesLookup_InvalidSsn_ReturnsValidationProblem(TestApiSource source)
    {
        var validSsn = (await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct))).PersonIdentifier.Value!.ToString();
        var invalidSsn = ChangeLastDigit(validSsn);

        SetSource(source);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/nameslookup",
            new Contracts.V1.PartyNamesLookup
            {
                Parties = [new Contracts.V1.PartyLookup { Ssn = invalidSsn }],
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, ValidationErrors.InvalidPersonNumber.ErrorCode);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyNamesLookup_InvalidSecondItem_ReturnsValidationProblem(TestApiSource source)
    {
        var org = await Setup((uow, ct) => uow.CreateOrg(cancellationToken: ct));
        var validOrgNo = org.OrganizationIdentifier.Value!.ToString();
        var invalidOrgNo = ChangeLastDigit(validOrgNo);

        SetSource(source);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/nameslookup",
            new Contracts.V1.PartyNamesLookup
            {
                Parties =
                [
                    new Contracts.V1.PartyLookup { OrgNo = validOrgNo },
                    new Contracts.V1.PartyLookup { OrgNo = invalidOrgNo },
                ],
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, ValidationErrors.InvalidOrganizationNumber.ErrorCode);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyNamesLookup_InvalidFirstItem_ReturnsValidationProblem(TestApiSource source)
    {
        var org = await Setup((uow, ct) => uow.CreateOrg(cancellationToken: ct));
        var validOrgNo = org.OrganizationIdentifier.Value!.ToString();
        var invalidOrgNo = ChangeLastDigit(validOrgNo);

        SetSource(source);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/nameslookup",
            new Contracts.V1.PartyNamesLookup
            {
                Parties =
                [
                    new Contracts.V1.PartyLookup { OrgNo = invalidOrgNo },
                    new Contracts.V1.PartyLookup { OrgNo = validOrgNo },
                ],
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, ValidationErrors.InvalidOrganizationNumber.ErrorCode);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyNamesLookup_MultipleInvalidItems_ReturnsAllValidationErrors(TestApiSource source)
    {
        SetSource(source);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/nameslookup",
            new Contracts.V1.PartyNamesLookup
            {
                Parties =
                [
                    new Contracts.V1.PartyLookup(),
                    new Contracts.V1.PartyLookup
                    {
                        Ssn = "01039012345",
                        OrgNo = "123456789",
                    },
                ],
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        var actual = await response.ShouldHaveJsonContent<AltinnValidationProblemDetails>();
        actual.ErrorCode.ShouldBe(StdProblemDescriptors.ErrorCodes.ValidationError);
        actual.Errors.ShouldContain(e => e.ErrorCode == StdValidationErrors.Required.ErrorCode);
        actual.Errors.ShouldContain(e => e.ErrorCode == ValidationErrors.MutuallyExclusive.ErrorCode);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyNamesLookup_NullItem_ReturnsValidationProblem(TestApiSource source)
    {
        SetSource(source);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/nameslookup",
            new
            {
                parties = new object?[]
                {
                    null,
                },
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        var actual = await response.ShouldHaveJsonContent<AltinnValidationProblemDetails>();
        actual.ErrorCode.ShouldBe(StdProblemDescriptors.ErrorCodes.ValidationError);

        actual.Errors.Count.ShouldBe(1);
        actual.Errors.Single().ShouldSatisfyAllConditions([
            e => e.ErrorCode.ShouldBe(StdValidationErrors.ErrorCodes.Required),
            e => e.Paths.ShouldBe(["/parties/0/ssn", "/parties/0/orgNo"], ignoreOrder: true),
        ]);
    }

    [Theory]
    [CombinatorialData]
    public async Task PostPartyNamesLookup_TooManyItems_ReturnsValidationProblem(TestApiSource source)
    {
        SetSource(source);

        var response = await HttpClient.PostAsJsonAsync(
            "register/api/v1/parties/nameslookup",
            new Contracts.V1.PartyNamesLookup
            {
                Parties = Enumerable.Repeat(new Contracts.V1.PartyLookup(), 1001).ToArray(),
            },
            CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        var actual = await response.ShouldHaveJsonContent<AltinnValidationProblemDetails>();
        actual.ErrorCode.ShouldBe(StdProblemDescriptors.ErrorCodes.ValidationError);

        actual.Errors.Count.ShouldBe(1);
        actual.Errors.Single().ShouldSatisfyAllConditions([
            e => e.ErrorCode.ShouldBe(ValidationErrors.TooManyItems.ErrorCode),
            e => e.Paths.ShouldBe(["/parties"], ignoreOrder: true),
        ]);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyIdentifiers_MixedIdentifierTypes_ReturnsPartyIdentifiers(TestApiSource source)
    {
        var (byId, byUuid, byOrgNo) = await Setup(async (uow, ct) =>
        {
            var person = await uow.CreatePerson(cancellationToken: ct);
            var secondPerson = await uow.CreatePerson(cancellationToken: ct);
            var organization = await uow.CreateOrg(cancellationToken: ct);

            return (person, secondPerson, organization);
        });

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            ConfigureA2PartyIdentifiersResponses([
                V1PartyMapper.ToV1Party(byId),
                V1PartyMapper.ToV1Party(byUuid),
                V1PartyMapper.ToV1Party(byOrgNo),
            ]);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"register/api/v1/parties/identifiers?ids={byId.PartyId.Value}&uuids={byUuid.PartyUuid.Value:D}&orgs={byOrgNo.OrganizationIdentifier.Value!}")
            .WithOrganizationToken(byOrgNo.OrganizationIdentifier.Value!, orgCode: "ttd")
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.Content.ReadFromJsonAsAsyncEnumerable<PartyIdentifiers>(cancellationToken: CancellationToken).ToListAsync(CancellationToken);
        var expected = new[]
        {
            CreatePartyIdentifiers(V1PartyMapper.ToV1Party(byId)),
            CreatePartyIdentifiers(V1PartyMapper.ToV1Party(byUuid)),
            CreatePartyIdentifiers(V1PartyMapper.ToV1Party(byOrgNo)),
        };

        actual.Count.ShouldBe(expected.Length);
        actual.ShouldContain(expected[0]);
        actual.ShouldContain(expected[1]);
        actual.ShouldContain(expected[2]);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyIdentifiers_InvalidIds_ReturnsValidationProblem(TestApiSource source)
    {
        SetSource(source);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "register/api/v1/parties/identifiers?ids=not-an-id")
            .WithOrganizationToken(OrganizationIdentifier.Parse("991825827"), orgCode: "ttd")
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, ValidationErrors.InvalidPartyId.ErrorCode, "$QUERY/ids");
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyIdentifiers_InvalidUuids_ReturnsValidationProblem(TestApiSource source)
    {
        SetSource(source);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "register/api/v1/parties/identifiers?uuids=not-a-uuid")
            .WithOrganizationToken(OrganizationIdentifier.Parse("991825827"), orgCode: "ttd")
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, ValidationErrors.InvalidPartyUuid.ErrorCode, "$QUERY/uuids");
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyIdentifiers_InvalidOrganizationNumbers_ReturnsValidationProblem(TestApiSource source)
    {
        SetSource(source);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "register/api/v1/parties/identifiers?orgs=not-an-org-number")
            .WithOrganizationToken(OrganizationIdentifier.Parse("991825827"), orgCode: "ttd")
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        await AssertValidationError(response, ValidationErrors.InvalidOrganizationNumber.ErrorCode, "$QUERY/orgs");
    }

    [Theory]
    [CombinatorialData]
    public async Task GetPartyIdentifiers_MixedQueryFormat_ReturnsPartyIdentifiers(TestApiSource source)
    {
        var (first, second, third) = await Setup(async (uow, ct) =>
        {
            var person = await uow.CreatePerson(cancellationToken: ct);
            var secondPerson = await uow.CreatePerson(cancellationToken: ct);
            var thirdPerson = await uow.CreatePerson(cancellationToken: ct);

            return (person, secondPerson, thirdPerson);
        });

        SetSource(source);
        if (source == TestApiSource.A2)
        {
            ConfigureA2PartyIdentifiersResponses([
                V1PartyMapper.ToV1Party(first),
                V1PartyMapper.ToV1Party(second),
                V1PartyMapper.ToV1Party(third),
            ]);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"register/api/v1/parties/identifiers?ids={first.PartyId.Value},{second.PartyId.Value}&ids={third.PartyId.Value}")
            .WithOrganizationToken(OrganizationIdentifier.Parse("991825827"), orgCode: "ttd")
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.Content.ReadFromJsonAsAsyncEnumerable<PartyIdentifiers>(cancellationToken: CancellationToken).ToListAsync(CancellationToken);
        var expected = new[]
        {
            CreatePartyIdentifiers(V1PartyMapper.ToV1Party(first)),
            CreatePartyIdentifiers(V1PartyMapper.ToV1Party(second)),
            CreatePartyIdentifiers(V1PartyMapper.ToV1Party(third)),
        };

        actual.Count.ShouldBe(expected.Length);
        actual.ShouldContain(expected[0]);
        actual.ShouldContain(expected[1]);
        actual.ShouldContain(expected[2]);
    }

    [Fact]
    public async Task GetPartyIdentifiers_EndpointSourceOverride_UsesEndpointSource()
    {
        var (person, organization) = await Setup(async (uow, ct) =>
        {
            var createdPerson = await uow.CreatePerson(cancellationToken: ct);
            var createdOrganization = await uow.CreateOrg(cancellationToken: ct);

            return (createdPerson, createdOrganization);
        });

        SetSource(TestApiSource.A2);
        SetSourceForEndpoint("parties/get-identifiers", TestApiSource.DB);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"register/api/v1/parties/identifiers?ids={person.PartyId.Value}&orgs={organization.OrganizationIdentifier.Value!}")
            .WithOrganizationToken(organization.OrganizationIdentifier.Value!, orgCode: "ttd")
            .WithPlatformToken();

        var response = await HttpClient.SendAsync(request, CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var actual = await response.Content.ReadFromJsonAsAsyncEnumerable<PartyIdentifiers>(cancellationToken: CancellationToken).ToListAsync(CancellationToken);
        var expected = new[]
        {
            CreatePartyIdentifiers(V1PartyMapper.ToV1Party(person)),
            CreatePartyIdentifiers(V1PartyMapper.ToV1Party(organization)),
        };

        actual.Count.ShouldBe(expected.Length);
        actual.ShouldContain(expected[0]);
        actual.ShouldContain(expected[1]);
    }

    private void SetSource(TestApiSource source)
    {
        SetSourceString(source switch
        {
            TestApiSource.A2 => "a2",
            TestApiSource.DB => "db",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        });
    }

    private void SetSourceForEndpoint(string endpointName, TestApiSource source)
    {
        var sourceString = source switch
        {
            TestApiSource.A2 => "a2",
            TestApiSource.DB => "db",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };

        Configuration.AddInMemoryCollection([
            new($"Altinn:register:ApiSource:Endpoints:{endpointName}", sourceString),
        ]);
    }

    private void SetSourceString(string sourceString)
    {
        Configuration.AddInMemoryCollection([
            new("Altinn:register:ApiSource:Default", sourceString),
        ]);
    }

    public static IEnumerable<PartyNamesLookupQueryParameterHandling> PartyNamesLookupQueryParameterHandlingData()
    {
        yield return new(string.Empty, false);
        yield return new("?partyComponentOption=", false);
        yield return new("?partyComponentOption=person-name", true);
    }

    public static IEnumerable<string> PartyNamesLookupInvalidQueryParameters()
    {
        yield return "?partyComponentOption=none";
        yield return "?partyComponentOption=non-existent";
    }

    private void ConfigureA2PartyLookupResponses(IEnumerable<Contracts.V1.Party> parties)
    {
        var partiesByLookupValue = parties
            .SelectMany(static party =>
            {
                var values = new List<KeyValuePair<string, Contracts.V1.Party>>(2);
                if (!string.IsNullOrEmpty(party.OrgNumber))
                {
                    values.Add(new(party.OrgNumber, party));
                }

                if (!string.IsNullOrEmpty(party.SSN))
                {
                    values.Add(new(party.SSN, party));
                }

                return values;
            })
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

        FakeHttpHandlers.For<IV1PartyService>().Fallback.Respond(
            async (HttpRequestMessage request, CancellationToken cancellationToken) =>
            {
                var lookupValue = await request.Content!.ReadFromJsonAsync<string>(cancellationToken: cancellationToken);
                if (lookupValue is not null && partiesByLookupValue.TryGetValue(lookupValue, out var party))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(party),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
    }

    private void ConfigureA2PartyIdentifiersResponses(IEnumerable<Contracts.V1.Party> parties)
    {
        var partiesById = parties
            .Where(static party => party.PartyId > 0)
            .ToDictionary(static party => party.PartyId);
        var partiesByUuid = parties
            .ToDictionary(static party => party.PartyUuid);
        var partiesByOrganizationNumber = parties
            .Where(static party => !string.IsNullOrEmpty(party.OrgNumber))
            .ToDictionary(static party => party.OrgNumber!, StringComparer.Ordinal);

        FakeHttpHandlers.For<IV1PartyService>()
            .Fallback.Respond(async (HttpRequestMessage request, CancellationToken cancellationToken) =>
            {
                var path = request.RequestUri?.AbsolutePath;
                object response = path switch
                {
                    { } value when value.EndsWith("/parties", StringComparison.Ordinal) => (await request.Content!.ReadFromJsonAsync<IReadOnlyList<int>>(cancellationToken: cancellationToken) ?? [])
                        .Where(partiesById.ContainsKey)
                        .Select(id => partiesById[id])
                        .ToArray(),
                    { } value when value.EndsWith("/parties/byuuid", StringComparison.Ordinal) => (await request.Content!.ReadFromJsonAsync<IReadOnlyList<Guid>>(cancellationToken: cancellationToken) ?? [])
                        .Where(partiesByUuid.ContainsKey)
                        .Select(uuid => partiesByUuid[uuid])
                        .ToArray(),
                    { } value when value.EndsWith("/parties/byssnorgnumber", StringComparison.Ordinal) => (await request.Content!.ReadFromJsonAsync<IReadOnlyList<string>>(cancellationToken: cancellationToken) ?? [])
                        .Where(partiesByOrganizationNumber.ContainsKey)
                        .Select(orgNumber => partiesByOrganizationNumber[orgNumber])
                        .ToArray(),
                    _ => throw new InvalidOperationException($"Unexpected A2 request path '{path}'."),
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(response),
                };
            });
    }

    private static PartyIdentifiers CreatePartyIdentifiers(Contracts.V1.Party party)
        => PartyIdentifiers.Create(party);

    private static string ChangeLastDigit(string value)
    {
        value.ShouldNotBeNullOrWhiteSpace();

        var last = value[^1];
        var replacement = last == '0' ? '1' : '0';
        return $"{value[..^1]}{replacement}";
    }

    private static async Task AssertValidationError(HttpResponseMessage response, ErrorCode expectedErrorCode, string? expectedPath = null)
    {
        var actual = await response.ShouldHaveJsonContent<AltinnValidationProblemDetails>();
        actual.ErrorCode.ShouldBe(StdProblemDescriptors.ErrorCodes.ValidationError);
        actual.Errors.ShouldContain(e => e.ErrorCode == expectedErrorCode);

        if (expectedPath is not null)
        {
            actual.Errors.ShouldContain(e => e.ErrorCode == expectedErrorCode && e.Paths.Contains(expectedPath));
        }
    }

    public readonly record struct PartyNamesLookupQueryParameterHandling(
        string QueryString,
        bool IncludePersonName);
}
