using System.Net;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Models;

namespace Altinn.Register.IntegrationTests.Metadata;

public class GetExternalRoleDefinitionsTests
    : IntegrationTestBase
{
    [Fact]
    public async Task GetExternalRoles_Returns_ExternalRoles()
    {
        var response = await HttpClient.GetAsync("/register/api/v2/internal/metadata/external-roles", TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var content = await response.ShouldHaveJsonContent<ListObject<ExternalRoleMetadata>>();
        var items = content.Items.ToList();
        items.ShouldNotBeEmpty();

        var allRoles = await GetRequiredService<IExternalRoleDefinitionPersistence>().GetAllRoleDefinitions(TestContext.Current.CancellationToken);
        items.Count.ShouldBe(allRoles.Length);

        foreach (var role in allRoles)
        {
            items.ShouldContain(r => r.Source == role.Source && r.Identifier == role.Identifier);
            var item = items.Find(r => r.Source == role.Source && r.Identifier == role.Identifier)!;

            item.Code.ShouldBe(role.Code);
            item.Name.ShouldBe(role.Name);
            item.Description.ShouldBe(role.Description);
        }
    }
}
