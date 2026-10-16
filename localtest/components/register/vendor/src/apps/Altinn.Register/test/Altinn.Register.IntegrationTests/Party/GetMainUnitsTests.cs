using System.Net;
using System.Net.Http.Json;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Models;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Party;

public class GetMainUnitsTests
    : IntegrationTestBase
{
    [Theory]
    [MemberData(nameof(MainUnitsPaths))]
    public async Task ByUuid(string path)
    {
        var (subUnit, mainUnit) = await Setup(async (uow, ct) =>
        {
            var units = await uow.CreateOrgs(2, cancellationToken: ct);
            var subUnit = units[0];
            var mainUnit = units[1];

            await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: subUnit.PartyUuid.Value, to: mainUnit.PartyUuid.Value, cancellationToken: ct);
            return (subUnit, mainUnit);
        });

        var requestContent = DataObject.Create(OrganizationUrn.PartyUuid.Create(subUnit.PartyUuid.Value));

        var response = await HttpClient.PostAsJsonAsync(path, requestContent, JsonOptions, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var content = await response.ShouldHaveJsonContent<ListObject<Organization>>();
        var items = content.Items.ToList();
        items.Count.ShouldBe(1);

        items[0].Uuid.ShouldBe(mainUnit.PartyUuid.Value);
    }

    [Theory]
    [MemberData(nameof(MainUnitsPaths))]
    public async Task ById(string path)
    {
        var (subUnit, mainUnit) = await Setup(async (uow, ct) =>
        {
            var units = await uow.CreateOrgs(2, cancellationToken: ct);
            var subUnit = units[0];
            var mainUnit = units[1];

            await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: subUnit.PartyUuid.Value, to: mainUnit.PartyUuid.Value, cancellationToken: ct);
            return (subUnit, mainUnit);
        });

        var requestContent = DataObject.Create(OrganizationUrn.PartyId.Create(subUnit.PartyId.Value));

        var response = await HttpClient.PostAsJsonAsync(path, requestContent, JsonOptions, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var content = await response.ShouldHaveJsonContent<ListObject<Organization>>();
        var items = content.Items.ToList();
        items.Count.ShouldBe(1);

        items[0].Uuid.ShouldBe(mainUnit.PartyUuid.Value);
    }

    [Theory]
    [MemberData(nameof(MainUnitsPaths))]
    public async Task ByOrganizationIdentifier(string path)
    {
        var (subUnit, mainUnit) = await Setup(async (uow, ct) =>
        {
            var units = await uow.CreateOrgs(2, cancellationToken: ct);
            var subUnit = units[0];
            var mainUnit = units[1];

            await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: subUnit.PartyUuid.Value, to: mainUnit.PartyUuid.Value, cancellationToken: ct);
            return (subUnit, mainUnit);
        });

        var requestContent = DataObject.Create(OrganizationUrn.OrganizationId.Create(subUnit.OrganizationIdentifier.Value!));

        var response = await HttpClient.PostAsJsonAsync(path, requestContent, JsonOptions, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var content = await response.ShouldHaveJsonContent<ListObject<Organization>>();
        var items = content.Items.ToList();
        items.Count.ShouldBe(1);

        items[0].Uuid.ShouldBe(mainUnit.PartyUuid.Value);
    }

    [Theory]
    [MemberData(nameof(MainUnitsPaths))]
    public async Task MultipleMainUnits(string path)
    {
        var (subUnit, mainUnit1, mainUnit2, mainUnit3) = await Setup(async (uow, ct) =>
        {
            var units = await uow.CreateOrgs(4, cancellationToken: ct);
            var subUnit = units[0];
            var mainUnit1 = units[1];
            var mainUnit2 = units[2];
            var mainUnit3 = units[3];

            await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: subUnit.PartyUuid.Value, to: mainUnit1.PartyUuid.Value, cancellationToken: ct);
            await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "ikke-naeringsdrivende-hovedenhet", from: subUnit.PartyUuid.Value, to: mainUnit2.PartyUuid.Value, cancellationToken: ct);
            await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "administrativ-enhet-offentlig-sektor", from: subUnit.PartyUuid.Value, to: mainUnit3.PartyUuid.Value, cancellationToken: ct);
            return (subUnit, mainUnit1, mainUnit2, mainUnit3);
        });

        var requestContent = DataObject.Create(OrganizationUrn.PartyUuid.Create(subUnit.PartyUuid.Value));

        var response = await HttpClient.PostAsJsonAsync(path, requestContent, JsonOptions, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var content = await response.ShouldHaveJsonContent<ListObject<Organization>>();
        var items = content.Items.ToList();
        items.Count.ShouldBe(3);

        items[0].Uuid.ShouldBe(mainUnit1.PartyUuid.Value);
        items[1].Uuid.ShouldBe(mainUnit2.PartyUuid.Value);
        items[2].Uuid.ShouldBe(mainUnit3.PartyUuid.Value);
    }

    [Theory]
    [MemberData(nameof(MainUnitsPaths))]
    public async Task NoMainUnits(string path)
    {
        var subUnit = await Setup((uow, ct) => uow.CreateOrg(cancellationToken: ct));

        var requestContent = DataObject.Create(OrganizationUrn.PartyUuid.Create(subUnit.PartyUuid.Value));

        var response = await HttpClient.PostAsJsonAsync(path, requestContent, JsonOptions, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var content = await response.ShouldHaveJsonContent<ListObject<Organization>>();

        content.Items.ShouldBeEmpty();
    }

    public static TheoryData<string> MainUnitsPaths => new([
        "register/api/v2/internal/parties/main-units",
        "register/api/v1/correspondence/parties/main-units",
    ]);
}
