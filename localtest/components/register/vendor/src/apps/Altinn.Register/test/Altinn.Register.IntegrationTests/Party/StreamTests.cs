using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Controllers.V2;
using Altinn.Register.Mapping;
using Altinn.Register.Models;
using Altinn.Register.TestUtils;
using Altinn.Register.TestUtils.TestData;
using Microsoft.Extensions.Options;

namespace Altinn.Register.IntegrationTests.Party;

[RunTestsSerially]
public class StreamTests
    : IntegrationTestBase
{
    private PartyController.Settings Settings => GetRequiredService<IOptionsMonitor<PartyController.Settings>>().CurrentValue;

    [Fact]
    public async Task EmptyStream()
    {
        var response = await HttpClient.GetAsync("/register/api/v2/internal/parties/stream", TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        var content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        content.Items.ShouldBeEmpty();
        content.Links.Next.ShouldBeNull();
        content.Stats.PageStart.ShouldBe(0UL);
        content.Stats.PageEnd.ShouldBe(0UL);
        content.Stats.SequenceMax.ShouldBe(0UL);
    }

    [Fact]
    public async Task SinglePage()
    {
        var orgs = await Setup(async (uow, ct) =>
        {
            return await uow.CreateOrgs(2, cancellationToken: ct);
        });

        var response = await HttpClient.GetAsync("/register/api/v2/internal/parties/stream", TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        var content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        var items = content.Items.ToList();
        items.Count.ShouldBe(2);

        items[0].Uuid.ShouldBe(orgs[0].PartyUuid.Value);
        items[1].Uuid.ShouldBe(orgs[1].PartyUuid.Value);

        content.Links.Next.ShouldNotBeNull();
        content.Links.Next.ShouldStartWith(BaseUrl + "register/api/v2/internal/parties/stream?");

        response = await HttpClient.GetAsync(content.Links.Next, TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        content.Items.ShouldBeEmpty();
        content.Links.Next.ShouldBeNull();
    }

    [Fact]
    public async Task MultiplePages()
    {
        var pageSize = Settings.PartyStreamPageSize;

        var orgs = await Setup(async (uow, ct) =>
        {
            return await uow.CreateOrgs((pageSize * 3) + (pageSize / 2), cancellationToken: ct);
        });

        /*********************************************
         * PAGE 1                                    *
         *********************************************/
        var response = await HttpClient.GetAsync("/register/api/v2/internal/parties/stream", TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        var content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        var items = content.Items.ToList();
        items.Count.ShouldBe(pageSize);

        for (var i = 0; i < items.Count; i++)
        {
            items[i].Uuid.ShouldBe(orgs[i].PartyUuid.Value);
        }

        content.Links.Next.ShouldNotBeNull();
        content.Links.Next.ShouldStartWith(BaseUrl + "register/api/v2/internal/parties/stream?");

        /*********************************************
         * PAGE 2                                    *
         *********************************************/
        response = await HttpClient.GetAsync(content.Links.Next, TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        items = content.Items.ToList();
        items.Count.ShouldBe(pageSize);

        for (var i = 0; i < items.Count; i++)
        {
            items[i].Uuid.ShouldBe(orgs[pageSize + i].PartyUuid.Value);
        }

        content.Links.Next.ShouldNotBeNull();
        content.Links.Next.ShouldStartWith(BaseUrl + "register/api/v2/internal/parties/stream?");

        /*********************************************
         * PAGE 3                                    *
         *********************************************/
        response = await HttpClient.GetAsync(content.Links.Next, TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        items = content.Items.ToList();
        items.Count.ShouldBe(pageSize);

        for (var i = 0; i < items.Count; i++)
        {
            items[i].Uuid.ShouldBe(orgs[(pageSize * 2) + i].PartyUuid.Value);
        }

        content.Links.Next.ShouldNotBeNull();
        content.Links.Next.ShouldStartWith(BaseUrl + "register/api/v2/internal/parties/stream?");

        /*********************************************
         * PAGE 4                                    *
         *********************************************/
        response = await HttpClient.GetAsync(content.Links.Next, TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        items = content.Items.ToList();
        items.Count.ShouldBe(pageSize / 2);

        for (var i = 0; i < items.Count; i++)
        {
            items[i].Uuid.ShouldBe(orgs[(pageSize * 3) + i].PartyUuid.Value);
        }

        content.Links.Next.ShouldNotBeNull();
        content.Links.Next.ShouldStartWith(BaseUrl + "register/api/v2/internal/parties/stream?");

        /*********************************************
         * PAGE 5                                    *
         *********************************************/
        response = await HttpClient.GetAsync(content.Links.Next, TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        content.Items.ShouldBeEmpty();
        content.Links.Next.ShouldBeNull();
    }

    [Fact]
    public async Task DefaultFields()
    {
        var (dbOrg, dbPers) = await Setup(async (uow, ct) =>
        {
            var org = await uow.CreateOrg(cancellationToken: ct);
            var pers = await uow.CreatePerson(cancellationToken: ct);

            return (org, pers);
        });

        var response = await HttpClient.GetAsync("/register/api/v2/internal/parties/stream", TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        var content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        var items = content.Items.ToList();
        items.Count.ShouldBe(2);

        var apiOrg = items[0];
        var apiPers = items[1];

        apiOrg.ShouldBeOfType<Organization>().ShouldSatisfyAllConditions(
            o => o.Uuid.ShouldBe(dbOrg.PartyUuid.Value),
            o => o.PartyId.ShouldBe(dbOrg.PartyId),
            o => o.DisplayName.ShouldBe(dbOrg.DisplayName),
            o => o.OrganizationIdentifier.ShouldBe(dbOrg.OrganizationIdentifier.Value),
            o => o.CreatedAt.ShouldBeUnset(),
            o => o.ModifiedAt.ShouldBeUnset(),
            o => o.IsDeleted.ShouldBeUnset(),
            o => o.VersionId.ShouldBe(dbOrg.VersionId.Value),
            o => o.UnitStatus.ShouldBeUnset(),
            o => o.UnitType.ShouldBeUnset(),
            o => o.TelephoneNumber.ShouldBeUnset(),
            o => o.MobileNumber.ShouldBeUnset(),
            o => o.FaxNumber.ShouldBeUnset(),
            o => o.EmailAddress.ShouldBeUnset(),
            o => o.InternetAddress.ShouldBeUnset(),
            o => o.MailingAddress.ShouldBeUnset(),
            o => o.BusinessAddress.ShouldBeUnset());

        apiPers.ShouldBeOfType<Person>().ShouldSatisfyAllConditions(
            p => p.Uuid.ShouldBe(dbPers.PartyUuid.Value),
            p => p.PartyId.ShouldBe(dbPers.PartyId),
            p => p.DisplayName.ShouldBe(dbPers.DisplayName),
            p => p.PersonIdentifier.ShouldBe(dbPers.PersonIdentifier.Value),
            p => p.CreatedAt.ShouldBeUnset(),
            p => p.ModifiedAt.ShouldBeUnset(),
            p => p.IsDeleted.ShouldBeUnset(),
            p => p.VersionId.ShouldBe(dbPers.VersionId.Value),
            p => p.FirstName.ShouldBeUnset(),
            p => p.MiddleName.ShouldBeUnset(),
            p => p.LastName.ShouldBeUnset(),
            p => p.ShortName.ShouldBeUnset(),
            p => p.Address.ShouldBeUnset(),
            p => p.MailingAddress.ShouldBeUnset(),
            p => p.DateOfBirth.ShouldBeUnset(),
            p => p.DateOfDeath.ShouldBeUnset());
    }

    [Fact]
    public async Task AllFields()
    {
        var (dbOrg, dbPers) = await Setup(async (uow, ct) =>
        {
            var org = await uow.CreateOrg(cancellationToken: ct);
            var pers = await uow.CreatePerson(cancellationToken: ct);

            return (org, pers);
        });

        var response = await HttpClient.GetAsync("/register/api/v2/internal/parties/stream?fields=person,party,org", TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        var content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        var nextLink = content.Links.Next.ShouldNotBeNull();
        nextLink.ShouldContain("fields=party,person,org");

        var items = content.Items.ToList();
        items.Count.ShouldBe(2);

        var apiOrg = items[0];
        var apiPers = items[1];

        apiOrg.ShouldBeOfType<Organization>().ShouldSatisfyAllConditions(
            o => o.Uuid.ShouldBe(dbOrg.PartyUuid.Value),
            o => o.PartyId.ShouldBe(dbOrg.PartyId),
            o => o.DisplayName.ShouldBe(dbOrg.DisplayName),
            o => o.OrganizationIdentifier.ShouldBe(dbOrg.OrganizationIdentifier.Value),
            o => o.CreatedAt.ShouldBe(dbOrg.CreatedAt),
            o => o.ModifiedAt.ShouldBe(dbOrg.ModifiedAt),
            o => o.IsDeleted.ShouldBe(dbOrg.IsDeleted),
            o => o.VersionId.ShouldBe(dbOrg.VersionId.Value),
            o => o.UnitStatus.ShouldBe(dbOrg.UnitStatus),
            o => o.UnitType.ShouldBe(dbOrg.UnitType),
            o => o.TelephoneNumber.ShouldBe(dbOrg.TelephoneNumber),
            o => o.MobileNumber.ShouldBe(dbOrg.MobileNumber),
            o => o.FaxNumber.ShouldBe(dbOrg.FaxNumber),
            o => o.EmailAddress.ShouldBe(dbOrg.EmailAddress),
            o => o.InternetAddress.ShouldBe(dbOrg.InternetAddress),
            o => o.MailingAddress.ShouldBe(dbOrg.MailingAddress.Select(static v => PartyMapper.ToPlatformModel(v))),
            o => o.BusinessAddress.ShouldBe(dbOrg.BusinessAddress.Select(static v => PartyMapper.ToPlatformModel(v))));

        apiPers.ShouldBeOfType<Person>().ShouldSatisfyAllConditions(
            p => p.Uuid.ShouldBe(dbPers.PartyUuid.Value),
            p => p.PartyId.ShouldBe(dbPers.PartyId),
            p => p.DisplayName.ShouldBe(dbPers.DisplayName),
            p => p.PersonIdentifier.ShouldBe(dbPers.PersonIdentifier.Value),
            p => p.CreatedAt.ShouldBe(dbPers.CreatedAt),
            p => p.ModifiedAt.ShouldBe(dbPers.ModifiedAt),
            p => p.IsDeleted.ShouldBe(dbPers.IsDeleted),
            p => p.VersionId.ShouldBe(dbPers.VersionId.Value),
            p => p.FirstName.ShouldBe(dbPers.FirstName),
            p => p.MiddleName.ShouldBe(dbPers.MiddleName),
            p => p.LastName.ShouldBe(dbPers.LastName),
            p => p.ShortName.ShouldBe(dbPers.ShortName),
            p => p.Address.ShouldBe(dbPers.Address.Select(static v => PartyMapper.ToPlatformModel(v))),
            p => p.MailingAddress.ShouldBe(dbPers.MailingAddress.Select(static v => PartyMapper.ToPlatformModel(v))),
            p => p.DateOfBirth.ShouldBe(dbPers.DateOfBirth),
            p => p.DateOfDeath.ShouldBe(dbPers.DateOfDeath));
    }

    [Fact]
    public async Task Person()
    {
        var party = await Setup(async (uow, ct) =>
        {
            var party = await uow.CreatePerson(cancellationToken: ct);

            return party;
        });

        var response = await HttpClient.GetAsync("/register/api/v2/internal/parties/stream?fields=person,party,org", TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        var content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        var nextLink = content.Links.Next.ShouldNotBeNull();
        nextLink.ShouldContain("fields=party,person,org");

        var items = content.Items.ToList();
        items.Count.ShouldBe(1);

        var apiParty = items[0];

        apiParty.ShouldBeOfType<Person>();
    }

    [Fact]
    public async Task Org()
    {
        var party = await Setup(async (uow, ct) =>
        {
            var party = await uow.CreateOrg(cancellationToken: ct);

            return party;
        });

        var response = await HttpClient.GetAsync("/register/api/v2/internal/parties/stream?fields=person,party,org", TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        var content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        var nextLink = content.Links.Next.ShouldNotBeNull();
        nextLink.ShouldContain("fields=party,person,org");

        var items = content.Items.ToList();
        items.Count.ShouldBe(1);

        var apiParty = items[0];

        apiParty.ShouldBeOfType<Organization>();
    }

    [Fact]
    public async Task SelfIdentified()
    {
        var party = await Setup(async (uow, ct) =>
        {
            var party = await uow.CreateSelfIdentifiedUser(cancellationToken: ct);

            return party;
        });

        var response = await HttpClient.GetAsync("/register/api/v2/internal/parties/stream?fields=person,party,org", TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        var content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        var nextLink = content.Links.Next.ShouldNotBeNull();
        nextLink.ShouldContain("fields=party,person,org");

        var items = content.Items.ToList();
        items.Count.ShouldBe(1);

        var apiParty = items[0];

        apiParty.ShouldBeOfType<SelfIdentifiedUser>();
    }

    [Fact]
    public async Task Enterprise()
    {
        var party = await Setup(async (uow, ct) =>
        {
            var owner = await uow.CreateOrg(cancellationToken: ct);
            var party = await uow.CreateEnterpriseUser(owner.PartyUuid.Value, cancellationToken: ct);

            return party;
        });

        var response = await HttpClient.GetAsync("/register/api/v2/internal/parties/stream?fields=person,party,org", TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        var content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        var nextLink = content.Links.Next.ShouldNotBeNull();
        nextLink.ShouldContain("fields=party,person,org");

        var items = content.Items.ToList();
        items.Count.ShouldBe(2);

        var apiParty = items.FirstOrDefault(i => i.Type == PartyType.EnterpriseUser);

        apiParty.ShouldBeOfType<EnterpriseUser>();
    }

    [Fact]
    public async Task SystemUser()
    {
        var party = await Setup(async (uow, ct) =>
        {
            var owner = await uow.CreateOrg(cancellationToken: ct);
            var party = await uow.CreateSystemUser(owner.PartyUuid.Value, cancellationToken: ct);

            return party;
        });

        var response = await HttpClient.GetAsync("/register/api/v2/internal/parties/stream?fields=person,party,org", TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        var content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        var nextLink = content.Links.Next.ShouldNotBeNull();
        nextLink.ShouldContain("fields=party,person,org");

        var items = content.Items.ToList();
        items.Count.ShouldBe(2);

        var apiParty = items.FirstOrDefault(i => i.Type == PartyType.SystemUser);

        apiParty.ShouldBeOfType<SystemUser>();
    }

    [Theory]
    [InlineData("person", typeof(Person))]
    [InlineData("organization", typeof(Organization))]
    [InlineData("self-identified-user", typeof(SelfIdentifiedUser))]
    [InlineData("system-user", typeof(SystemUser))]
    [InlineData("enterprise-user", typeof(EnterpriseUser))]
    public async Task FilterType_Single(string partyType, Type expectedType)
    {
        await Setup(async (uow, ct) =>
        {
            await uow.CreatePerson(cancellationToken: ct);
            var org = await uow.CreateOrg(cancellationToken: ct);
            await uow.CreateSelfIdentifiedUser(cancellationToken: ct);
            await uow.CreateSystemUser(owner: org.PartyUuid.Value, cancellationToken: ct);
            await uow.CreateEnterpriseUser(owner: org.PartyUuid.Value, cancellationToken: ct);
        });

        using var response = await HttpClient.GetAsync($"/register/api/v2/internal/parties/stream?types={partyType}", TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        var content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        var nextLink = content.Links.Next.ShouldNotBeNull();
        nextLink.ShouldContain($"types={partyType}");

        var items = content.Items.ToList();
        items.Count.ShouldBe(1);

        var apiParty = items[0];
        apiParty.ShouldBeOfType(expectedType);
    }

    [Fact]
    public async Task FilterType_Multiple()
    {
        await Setup(async (uow, ct) =>
        {
            await uow.CreatePerson(cancellationToken: ct);
            var org = await uow.CreateOrg(cancellationToken: ct);
            await uow.CreateSelfIdentifiedUser(cancellationToken: ct);
            await uow.CreateSystemUser(owner: org.PartyUuid.Value, cancellationToken: ct);
            await uow.CreateEnterpriseUser(owner: org.PartyUuid.Value, cancellationToken: ct);
        });

        using var response = await HttpClient.GetAsync("/register/api/v2/internal/parties/stream?types=enterprise-user,system-user", TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        var content = await response.ShouldHaveJsonContent<ItemStream<Contracts.Party>>();

        var nextLink = content.Links.Next.ShouldNotBeNull();
        nextLink.ShouldContain($"types=system-user,enterprise-user");

        var items = content.Items.ToList();
        items.Count.ShouldBe(2);

        items.ShouldContain(p => p is EnterpriseUser);
        items.ShouldContain(p => p is SystemUser);
    }
}
