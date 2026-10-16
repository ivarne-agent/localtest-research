using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Models;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Party.ExternalRoleAssignments;

public class CorrespondenceTests
    : IntegrationTestBase
{
    [Fact]
    public async Task NoAssignments()
    {
        var response = await HttpClient.GetAsync($"/register/api/v1/correspondence/parties/{Guid.NewGuid()}/roles/correspondence-roles", TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        var content = await response.ShouldHaveJsonContent<Paginated<ExternalRoleAssignment>>();

        content.Items.ShouldBeEmpty();
        content.Links.Next.ShouldBeNull();
    }

    [Fact]
    public async Task Assignments()
    {
        List<string> roles = [
            "innehaver",
            "komplementar",
            "styreleder",
            "deltaker-delt-ansvar",
            "deltaker-fullt-ansvar",
            "bestyrende-reder",
            "daglig-leder",
            "bostyrer",
            "kontaktperson-ados",
            "norsk-representant",

            // Not correspondence roles
            "kontaktperson",
        ];

        var (org, persons, orgs) = await Setup(async (uow, ct) =>
        {
            var org = await uow.CreateOrg(cancellationToken: ct);
            var persons = await uow.CreatePeople(roles.Count, cancellationToken: ct);
            var orgs = await uow.CreateOrgs(roles.Count, cancellationToken: ct);

            for (var i = 0; i < roles.Count; i++)
            {
                var role = roles[i];
                var toPers = persons[i];
                var toOrg = orgs[i];

                await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, role, org.PartyUuid.Value, toPers.PartyUuid.Value, ct);
                await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, role, org.PartyUuid.Value, toOrg.PartyUuid.Value, ct);
            }

            return (org, persons, orgs);
        });

        var response = await HttpClient.GetAsync($"/register/api/v1/correspondence/parties/{org.PartyUuid}/roles/correspondence-roles", TestContext.Current.CancellationToken);

        await response.ShouldHaveSuccessStatusCode();
        var content = await response.ShouldHaveJsonContent<Paginated<ExternalRoleAssignment>>();

        var items = content.Items.ToList();
        items.Count.ShouldBe((roles.Count - 1) * 2); // minus "kontaktperson", times two (person + org)

        for (var i = 0; i < roles.Count; i++)
        {
            var role = roles[i];
            var toPers = persons[i];
            var toOrg = orgs[i];

            if (role == "kontaktperson")
            {
                items.ShouldNotContain(i => i.Role.Identifier == role);
                continue;
            }

            items.ShouldContain(i => i.Role.Identifier == role && i.ToParty.Uuid == toPers.PartyUuid.Value);
            items.ShouldContain(i => i.Role.Identifier == role && i.ToParty.Uuid == toOrg.PartyUuid.Value);
        }
    }
}
