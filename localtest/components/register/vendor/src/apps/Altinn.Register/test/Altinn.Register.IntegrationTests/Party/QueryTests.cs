using System.Net;
using System.Net.Http.Json;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Controllers.V2;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Models;
using Altinn.Register.TestUtils.TestData;
using Altinn.Urn;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Register.IntegrationTests.Party;

public class QueryTests
    : IntegrationTestBase
{
    [Fact]
    public async Task EmptyRequest()
    {
        var response = await HttpClient.PostAsJsonAsync("register/api/v2/internal/parties/query", ListObject.Create<PartyUrn>([]), JsonOptions, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var content = await response.ShouldHaveJsonContent<ListObject<Contracts.Party>>();

        content.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task EmptyResponse()
    {
        var response = await HttpClient.PostAsJsonAsync("register/api/v2/internal/parties/query", ListObject.Create<PartyUrn>([PartyUrn.PartyId.Create(1)]), JsonOptions, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.PartialContent);
        var content = await response.ShouldHaveJsonContent<ListObject<Contracts.Party>>();

        content.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task MultipleIdentifiersSameParty()
    {
        var party = await Setup(async (uow, ct) =>
        {
            var userIds = await uow.GetRequiredService<RegisterTestDataGenerator>().GetNextUserIds(1, ct);
            var user = new PartyUserRecord(userId: userIds[0], username: Guid.NewGuid().ToString());
            var person = await uow.CreatePerson(user: user, cancellationToken: ct);
            return person;
        });

        var requestContent = ListObject.Create<PartyUrn>([
            PartyUrn.PartyUuid.Create(party.PartyUuid.Value),
            PartyUrn.PartyId.Create(party.PartyId.Value),
            PartyUrn.PersonId.Create(party.PersonIdentifier.Value!),
            PartyUrn.UserId.Create(party.UserIds.CurrentValue.Value),
            PartyUrn.Username.Create(UrnEncoded.Create(party.Usernames.CurrentValue.Value!)),

            // repeat again for good measure
            PartyUrn.PartyUuid.Create(party.PartyUuid.Value),
            PartyUrn.PartyId.Create(party.PartyId.Value),
            PartyUrn.PersonId.Create(party.PersonIdentifier.Value!),
            PartyUrn.UserId.Create(party.UserIds.CurrentValue.Value),
            PartyUrn.Username.Create(UrnEncoded.Create(party.Usernames.CurrentValue.Value!)),
        ]);

        var response = await HttpClient.PostAsJsonAsync("register/api/v2/internal/parties/query", requestContent, JsonOptions, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var content = await response.ShouldHaveJsonContent<ListObject<Contracts.Party>>();

        var item = content.Items.ShouldHaveSingleItem();
        item.Uuid.ShouldBe(party.PartyUuid.Value);
    }

    [Fact]
    public async Task MissingParties()
    {
        var party = await Setup((uow, ct) => uow.CreatePerson(cancellationToken: ct));

        var requestContent = ListObject.Create<PartyUrn>([
            PartyUrn.PartyId.Create(party.PartyId.Value),
            PartyUrn.PartyId.Create(party.PartyId.Value + 1),
            PartyUrn.PartyId.Create(party.PartyId.Value + 2),
        ]);

        var response = await HttpClient.PostAsJsonAsync("register/api/v2/internal/parties/query", requestContent, JsonOptions, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.PartialContent);
        var content = await response.ShouldHaveJsonContent<ListObject<Contracts.Party>>();

        content.Items.ShouldHaveSingleItem().Uuid.ShouldBe(party.PartyUuid.Value);
    }

    [Fact]
    public async Task TooManyInRequest()
    {
        var items = new List<PartyUrn>(PartyController.PARTY_QUERY_MAX_ITEMS + 1);
        for (uint i = 0; i < PartyController.PARTY_QUERY_MAX_ITEMS + 1; i++)
        {
            items.Add(PartyUrn.PartyId.Create(i));
        }

        var requestContent = ListObject.Create(items);

        var response = await HttpClient.PostAsJsonAsync("register/api/v2/internal/parties/query", requestContent, JsonOptions, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
    }
}
