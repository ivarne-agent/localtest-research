using System.Net;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Party;

public class GetPartyByUUIDTests
    : IntegrationTestBase
{
    [Fact]
    public async Task NonExistentParty()
    {
        var response = await HttpClient.GetAsync($"register/api/v2/internal/parties/{Guid.Empty}", CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        var content = await response.ShouldHaveJsonContent<AltinnProblemDetails>();

        content.ErrorCode.ShouldBe(Problems.ReferencedPartyNotFound.ErrorCode);
    }

    [Fact]
    public async Task DefaultFields()
    {
        var party = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));

        var response = await HttpClient.GetAsync($"register/api/v2/internal/parties/{party.PartyUuid.Value}", CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var content = await response.ShouldHaveJsonContent<Contracts.Party>();
        var person = content.ShouldBeOfType<Person>();

        content.Uuid.ShouldBe(party.PartyUuid.Value);
        content.PartyId.ShouldBe(party.PartyId);
        content.DisplayName.ShouldBe(party.DisplayName);
        content.VersionId.ShouldNotBe(0UL);
        content.CreatedAt.ShouldBeUnset();
        content.ModifiedAt.ShouldBeUnset();
        content.User.ShouldBeUnset();

        person.PersonIdentifier.ShouldBe(party.PersonIdentifier.Value);
        person.FirstName.ShouldBeUnset();
        person.MiddleName.ShouldBeUnset();
        person.LastName.ShouldBeUnset();
    }

    [Fact]
    public async Task CustomFields()
    {
        var party = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));

        var response = await HttpClient.GetAsync($"register/api/v2/internal/parties/{party.PartyUuid.Value}?fields=party,person", CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var content = await response.ShouldHaveJsonContent<Contracts.Party>();
        var person = content.ShouldBeOfType<Person>();

        content.Uuid.ShouldBe(party.PartyUuid.Value);
        content.PartyId.ShouldBe(party.PartyId);
        content.VersionId.ShouldNotBe(0UL);
        content.DisplayName.ShouldBe(party.DisplayName);
        content.CreatedAt.ShouldBe(party.CreatedAt);
        content.ModifiedAt.ShouldBe(party.ModifiedAt);
        content.User.ShouldBeUnset();

        person.PersonIdentifier.ShouldBe(party.PersonIdentifier.Value);
        person.FirstName.ShouldBe(party.FirstName);
        person.MiddleName.ShouldBe(party.MiddleName);
        person.LastName.ShouldBe(party.LastName);
    }
}
