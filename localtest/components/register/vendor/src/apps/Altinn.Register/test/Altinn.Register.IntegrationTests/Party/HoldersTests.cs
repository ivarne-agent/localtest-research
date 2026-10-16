using System.Net;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.Models;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Party;

public class HoldersTests
    : IntegrationTestBase
{
    [Theory]
    [MemberData(nameof(HoldersRoleIdentifiers))]
    public async Task Empty(string roleIdentifier, string apiVersion)
    {
        var org = await Setup(async (uow, ct) =>
        {
            return await uow.CreateOrg(cancellationToken: ct);
        });

        var response = await HttpClient.GetAsync($"register/api/{apiVersion}/internal/parties/{org.PartyUuid.Value}/holders/ccr/{roleIdentifier}", TestContext.Current.CancellationToken);
        await response.ShouldHaveStatusCode(HttpStatusCode.OK);

        var content = await response.ShouldHaveJsonContent<ListObject<Contracts.Party>>();
        content.Items.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(HoldersRoleIdentifiers))]
    public async Task Roles(string roleIdentifier, string apiVersion)
    {
        var (org1, org2, org3, org4, pers1, pers2) = await Setup(async (uow, ct) =>
        {
            var role2 = await uow.CreateFakeRoleDefinition(ExternalRoleSource.CentralCoordinatingRegister, "fake", ct);

            var org1 = await uow.CreateOrg(cancellationToken: ct);
            var org2 = await uow.CreateOrg(cancellationToken: ct);
            var org3 = await uow.CreateOrg(cancellationToken: ct);
            var org4 = await uow.CreateOrg(cancellationToken: ct);
            var pers1 = await uow.CreatePerson(cancellationToken: ct);
            var pers2 = await uow.CreatePerson(cancellationToken: ct);

            var persistence = uow.GetPartyExternalRolePersistence();

            await persistence.UpsertExternalRolesFromPartyBySource(
                commandId: Guid.CreateVersion7(),
                partyUuid: org1.PartyUuid.Value,
                roleSource: ExternalRoleSource.CentralCoordinatingRegister,
                update: PartyExternalRoleAssignmentsUpdate.CreateFull([
                    new(roleIdentifier, org1.PartyUuid.Value),
                    new(roleIdentifier, org2.PartyUuid.Value),
                    new(roleIdentifier, org3.PartyUuid.Value),
                    new(roleIdentifier, pers1.PartyUuid.Value),
                    new(role2.Identifier, org1.PartyUuid.Value),
                    new(role2.Identifier, org3.PartyUuid.Value),
                    new(role2.Identifier, org4.PartyUuid.Value),
                    new(role2.Identifier, pers1.PartyUuid.Value),
                    new(role2.Identifier, pers2.PartyUuid.Value),
                ]),
                cancellationToken: ct);

            return (org1, org2, org3, org4, pers1, pers2);
        });

        var response = await HttpClient.GetAsync($"register/api/{apiVersion}/internal/parties/{org1.PartyUuid.Value}/holders/ccr/{roleIdentifier}", TestContext.Current.CancellationToken);
        await response.ShouldHaveStatusCode(HttpStatusCode.OK);

        var content = await response.ShouldHaveJsonContent<ListObject<Contracts.Party>>();
        var items = content.Items.ToList();
        items.Count.ShouldBe(4);
        items.ShouldContain(p => p.Uuid == org1.PartyUuid.Value);
        items.ShouldContain(p => p.Uuid == org2.PartyUuid.Value);
        items.ShouldContain(p => p.Uuid == org3.PartyUuid.Value);
        items.ShouldContain(p => p.Uuid == pers1.PartyUuid.Value);
    }

    public static TheoryData<string, string> HoldersRoleIdentifiers => new MatrixTheoryData<string, string>(
        ["daglig-leder"],
        ["v1", "v2"]);
}
