using System.Net;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.Models;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Party;

public class CustomersTests
    : IntegrationTestBase
{
    [Theory]
    [MemberData(nameof(CustomerRoleIdentifiers))]
    public async Task Empty(string roleIdentifier, string apiVersion)
    {
        var org = await Setup(async (uow, ct) =>
        {
            return await uow.CreateOrg(cancellationToken: ct);
        });

        var response = await HttpClient.GetAsync($"register/api/{apiVersion}/internal/parties/{org.PartyUuid.Value}/customers/ccr/{roleIdentifier}", TestContext.Current.CancellationToken);
        await response.ShouldHaveStatusCode(HttpStatusCode.OK);

        var content = await response.ShouldHaveJsonContent<ListObject<Contracts.Party>>();
        content.Items.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(CustomerRoleIdentifiers))]
    public async Task Roles(string roleIdentifier, string apiVersion)
    {
        var (org1, org2, org3, org4) = await Setup(async (uow, ct) =>
        {
            var role2 = await uow.CreateFakeRoleDefinition(ExternalRoleSource.CentralCoordinatingRegister, "fake", ct);

            var org1 = await uow.CreateOrg(cancellationToken: ct);
            var org2 = await uow.CreateOrg(cancellationToken: ct);
            var org3 = await uow.CreateOrg(cancellationToken: ct);
            var org4 = await uow.CreateOrg(cancellationToken: ct);

            var persistence = uow.GetPartyExternalRolePersistence();

            await persistence.UpsertExternalRolesFromPartyBySource(
                commandId: Guid.CreateVersion7(),
                partyUuid: org2.PartyUuid.Value,
                roleSource: ExternalRoleSource.CentralCoordinatingRegister,
                update: PartyExternalRoleAssignmentsUpdate.CreateFull([
                    new(roleIdentifier, org1.PartyUuid.Value),
                    new(role2.Identifier, org1.PartyUuid.Value),
                ]),
                cancellationToken: ct);

            await persistence.UpsertExternalRolesFromPartyBySource(
                commandId: Guid.CreateVersion7(),
                partyUuid: org3.PartyUuid.Value,
                roleSource: ExternalRoleSource.CentralCoordinatingRegister,
                update: PartyExternalRoleAssignmentsUpdate.CreateFull([
                    new(roleIdentifier, org1.PartyUuid.Value),
                ]),
                cancellationToken: ct);

            await persistence.UpsertExternalRolesFromPartyBySource(
                commandId: Guid.CreateVersion7(),
                partyUuid: org4.PartyUuid.Value,
                roleSource: ExternalRoleSource.CentralCoordinatingRegister,
                update: PartyExternalRoleAssignmentsUpdate.CreateFull([
                    new(role2.Identifier, org1.PartyUuid.Value),
                ]),
                cancellationToken: ct);

            return (org1, org2, org3, org4);
        });

        var response = await HttpClient.GetAsync($"register/api/{apiVersion}/internal/parties/{org1.PartyUuid.Value}/customers/ccr/{roleIdentifier}", TestContext.Current.CancellationToken);
        await response.ShouldHaveStatusCode(HttpStatusCode.OK);

        var content = await response.ShouldHaveJsonContent<ListObject<Contracts.Party>>();
        var items = content.Items.ToList();
        items.Count.ShouldBe(2);
        items.ShouldContain(p => p.Uuid == org2.PartyUuid.Value);
        items.ShouldContain(p => p.Uuid == org3.PartyUuid.Value);
    }

    public static TheoryData<string, string> CustomerRoleIdentifiers => new MatrixTheoryData<string, string>(
        ["revisor", "regnskapsforer", "forretningsforer"],
        ["v1", "v2"]);
}
