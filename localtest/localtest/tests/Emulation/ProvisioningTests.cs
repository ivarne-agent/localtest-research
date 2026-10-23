using System.Net;
using System.Text;
using System.Text.Json;
using Localtest.Emulated;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Localtest.Tests.Emulation;

/// <summary>
/// Registration in minimal mode: the same provisioning steps, with Storage's over HTTP (a fake Storage here) and the
/// others into memory; the Studio token minted in process; deprovisioning removes the app's test data and policy.
/// </summary>
public sealed class ProvisioningTests
{
    private const string TestData = """{"persons":[{"ssn":"","userId":7001,"partyId":570001,"email":"app-user@example.com","partyRoles":{}}],"orgs":[]}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAppIsProvisionedIntoMemoryAndStorageAndRemovedAgain()
    {
        await using var app = await FakeServer.Start(context =>
        {
            context.Response.ContentType = "application/json";
            return (context.Request.Path.Value ?? string.Empty) switch
            {
                "/ttd/fake-app/api/v1/applicationmetadata" => context.Response.WriteAsync("""{"id":"ttd/fake-app","org":"ttd"}"""),
                "/ttd/fake-app/api/v1/meta/authorizationpolicy" => context.Response.WriteAsync("<Policy RuleId=\"[ORG]:[app]\"/>"),
                "/ttd/fake-app/api/v1/applicationlanguages" => context.Response.WriteAsync("""[{"language":"nb"}]"""),
                "/ttd/fake-app/api/v1/texts/nb" => context.Response.WriteAsync("""{"language":"nb","resources":[]}"""),
                "/ttd/fake-app/testData.json" => context.Response.WriteAsync(TestData),
                _ => NotFound(context),
            };
        });
        await using var storage = await FakeServer.Start(context =>
        {
            context.Response.StatusCode = context.Request.Method == "POST" ? StatusCodes.Status201Created : StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        await using var frontDoor = new MinimalFrontDoor(new Dictionary<string, string?> { ["storage"] = storage.Origin });
        using var client = frontDoor.Client();

        using var put = await client.PutAsync("/localtest/api/apps/ttd/fake-app", MinimalFrontDoor.Json($$"""{"url":"{{app.Origin}}"}"""), Ct);
        var body = await put.Content.ReadAsStringAsync(Ct);
        Assert.True(put.StatusCode == HttpStatusCode.OK, body);
        using (var route = JsonDocument.Parse(body))
        {
            Assert.Equal("succeeded", route.RootElement.GetProperty("provisioning").GetProperty("state").GetString());
        }

        // Storage got metadata and texts with the Studio token minted in process; no other service was called.
        Assert.Equal(["POST /storage/api/v1/applications?appId=ttd/fake-app", "POST /storage/api/v1/applications/ttd/fake-app/texts"], storage.Calls);
        var studio = storage.Requests.First().Authorization["Bearer ".Length..];
        Assert.Equal("studio.designer", MinimalFrontDoor.Claims(studio).GetProperty("urn:altinn:app").GetString());

        // Every registered app has test data: only the apps' users (testdata/README.md).
        var store = frontDoor.Services.GetRequiredService<TestDataStore>();
        Assert.Equal([7001], store.Current.Users.Select(u => u.UserId));
        Assert.Equal("<Policy RuleId=\"ttd:fake-app\"/>", frontDoor.Services.GetRequiredService<PolicyStore>().Get("ttd", "fake-app"));
        Assert.Contains("app-user@example.com", await client.GetStringAsync("/", Ct), StringComparison.Ordinal);

        // The routes are in memory (Proxy__AppRoutesFile=): nothing is written.
        Assert.Empty(Directory.GetFiles(frontDoor.DataDirectory, "*.json"));

        using var delete = await client.DeleteAsync("/localtest/api/apps/ttd/fake-app", Ct);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(8, store.Current.Users.Count);
        Assert.Null(frontDoor.Services.GetRequiredService<PolicyStore>().Get("ttd", "fake-app"));
        using var again = await client.DeleteAsync("/localtest/api/apps/ttd/fake-app", Ct);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Empty(await client.GetFromJsonArray("/localtest/api/apps"));
    }

    private static Task NotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }
}

internal static class ClientExtensions
{
    public static async Task<JsonElement[]> GetFromJsonArray(this HttpClient client, string path)
    {
        using var document = JsonDocument.Parse(await client.GetStringAsync(path, TestContext.Current.CancellationToken));
        return [.. document.RootElement.EnumerateArray().Select(e => e.Clone())];
    }
}
