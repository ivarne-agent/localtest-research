using System.Collections.Concurrent;
using System.Net;
using Localtest.Home.Instances;
using Localtest.Proxy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Localtest.Tests;

/// <summary>
/// The instances page (<c>/Home/Instances</c>, Home/Instances/) against a fake Storage, Authentication and Register on
/// loopback ports: the form, the parameters and token passed to Storage, the list, the instance page with its links and
/// similar instances, and the downloads.
/// </summary>
public sealed class InstancesPageTests : IAsyncLifetime
{
    private const string Running = "8e7a5c2e-3f1b-4b8e-9d6a-1c2b3d4e5f60";
    private const string Completed = "0b1c2d3e-4f50-4617-8293-a4b5c6d7e8f9";
    private const string DataGuid = "11111111-2222-4333-8444-555555555555";

    private static readonly string RunningInstance = $$$"""
        {"id":"500000/{{{Running}}}","appId":"ttd/app","org":"ttd","instanceOwner":{"partyId":"500000","organisationNumber":"897069650"},
         "created":"2026-10-26T10:00:00Z","lastChanged":"2026-10-26T10:05:00Z",
         "process":{"started":"2026-10-26T10:00:00Z","startEvent":"StartEvent_1","currentTask":{"elementId":"Task_1","name":"Utfylling","altinnTaskType":"data"}},
         "status":{"isArchived":false,"isSoftDeleted":false,"isHardDeleted":false},
         "data":[{"id":"{{{DataGuid}}}","dataType":"attachment","filename":"report.pdf","contentType":"application/pdf","size":4}]}
        """;

    private static readonly string CompletedInstance = $$$"""
        {"id":"512345/{{{Completed}}}","appId":"ttd/app","org":"ttd","instanceOwner":{"partyId":"512345","personNumber":"01017512345"},
         "created":"2026-10-25T09:00:00Z","lastChanged":"2026-10-25T09:30:00Z",
         "process":{"started":"2026-10-25T09:00:00Z","ended":"2026-10-25T09:30:00Z","endEvent":"EndEvent_1"},
         "status":{"isArchived":true,"archived":"2026-10-25T09:30:00Z","isSoftDeleted":false,"isHardDeleted":false},
         "dataValues":{"dialog.id":"01937c7e-0000-7000-8000-000000000001"},"data":[]}
        """;

    private FakeServer _storage = null!;
    private FakeServer _authentication = null!;
    private FakeServer _register = null!;
    private readonly ConcurrentQueue<string> _identifiers = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _storage = await FakeServer.Start(async context =>
        {
            _identifiers.Enqueue(context.Request.Headers[StorageQuery.InstanceOwnerIdentifierHeader].ToString());
            var path = context.Request.Path.Value ?? string.Empty;
            if (context.Request.Headers.Authorization.Count == 0)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (path == "/storage/api/v1/instances")
            {
                if (context.Request.Query["searchString"] == "broken")
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("Org or AppId must be defined.");
                    return;
                }

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync($$$"""
                    {"count":2,"self":"http://local.altinn.cloud:8000/storage/api/v1/instances?org=ttd",
                     "next":"http://local.altinn.cloud:8000/storage/api/v1/instances?org=ttd&continuationToken=638%253B42",
                     "instances":[{{{RunningInstance}}},{{{CompletedInstance}}}]}
                    """);
            }
            else if (path == $"/storage/api/v1/instances/500000/{Running}")
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(RunningInstance);
            }
            else if (path == $"/storage/api/v1/instances/512345/{Completed}")
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(CompletedInstance);
            }
            else if (path == $"/storage/api/v1/instances/500000/{Running}/data/{DataGuid}")
            {
                context.Response.ContentType = "application/pdf";
                context.Response.Headers.ContentDisposition = "attachment; filename=report.pdf";
                await context.Response.WriteAsync("%PDF");
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        });
        _authentication = await FakeServer.Start(context =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            switch (path)
            {
                case "/authentication/api/v1/exchange/maskinporten":
                    return context.Response.WriteAsync("\"org-token\"");
                case "/authentication/api/v1/authentication":
                    // As production: off to the identity provider with the callback as redirect_uri.
                    context.Response.Redirect("http://local.altinn.cloud:8000/localtest/api/idporten/authorize?client_id=c&state=s&nonce=n&code_challenge=x&redirect_uri="
                        + Uri.EscapeDataString("http://local.altinn.cloud:8000/authentication/api/v1/upstream/callback"));
                    return Task.CompletedTask;
                case "/authentication/api/v1/upstream/callback":
                    context.Response.Headers.Append("Set-Cookie", "AltinnStudioRuntime=user-token; path=/; httponly");
                    context.Response.Redirect("http://local.altinn.cloud:8000/");
                    return Task.CompletedTask;
                default:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
            }
        });
        _register = await FakeServer.Start(context =>
        {
            context.Response.ContentType = "application/json";
            return context.Request.Path.Value switch
            {
                "/register/api/localtest/test-users" => context.Response.WriteAsync("""
                    [{"userId":12345,"partyId":512345,"partyUuid":"4f9b6f6e-7a3c-4a59-9c8e-0d1f2a3b4c5d","ssn":"01017512345","displayName":"Sophie Salt",
                      "partyType":"person","parties":[{"partyId":512345,"partyUuid":"4f9b6f6e-7a3c-4a59-9c8e-0d1f2a3b4c5d","displayName":"Sophie Salt"},
                        {"partyId":500000,"partyUuid":"5a9b6f6e-7a3c-4a59-9c8e-0d1f2a3b4c5d","displayName":"DDG Fitness"}],
                      "actingFor":[{"partyId":500000,"partyType":"organization","orgNumber":"897069650","ssn":null}]}]
                    """),
                "/register/api/localtest/organizations" => context.Response.WriteAsync("""[{"partyId":500000,"orgNumber":"897069650"}]"""),
                _ => context.Response.WriteAsync("[]"),
            };
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _storage.DisposeAsync();
        await _authentication.DisposeAsync();
        await _register.DisposeAsync();
    }

    private FrontDoor Door(params string[] running)
    {
        var services = new Dictionary<string, string?>
        {
            ["storage"] = _storage.Origin,
            ["authentication"] = _authentication.Origin,
            ["register"] = _register.Origin,
        };
        foreach (var service in running)
        {
            // Only the address's presence counts for the links.
            services[service] = "http://127.0.0.1:1";
        }

        return new FrontDoor(services);
    }

    private static async Task<(HttpStatusCode Status, string Body)> Get(FrontDoor door, string path)
    {
        using var response = await door.Client().GetAsync(path, Ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task TheFormNamesEveryStorageParameterAndTheTokenFields()
    {
        await using var door = Door();
        var (status, body) = await Get(door, "/Home/Instances");

        Assert.Equal(HttpStatusCode.OK, status);
        foreach (var name in StorageQuery.Single.Append(StorageQuery.InstanceOwnerIdentifier).Concat(TokenChoice.Parameters))
        {
            Assert.Contains($"name=\"{name}\"", body);
        }

        foreach (var date in StorageQuery.Dates)
        {
            Assert.Contains($"'{date}'", body);
        }

        Assert.Contains("href=\"/Home/Instances\"", (await Get(door, "/")).Body);
    }

    [Fact]
    public async Task TheListPassesStoragesParametersTheHeaderAndThePastedToken()
    {
        await using var door = Door();
        var (status, body) = await Get(door,
            "/Home/Instances/Results?auth=token&token=Bearer%20pasted&org=ttd&appId=ttd%2Fapp&process.isComplete=false&status.isArchived=&unknown=x"
            + "&created=gte%3A2026-10-01&created=lt%3A2026-11-01&order=asc%3AlastChanged&size=2&instanceOwner.identifier=person%3A01017512345");

        Assert.Equal(HttpStatusCode.OK, status);
        var request = Assert.Single(_storage.Requests);
        Assert.Equal("GET /storage/api/v1/instances?org=ttd&appId=ttd%2Fapp&process.isComplete=false&order=asc%3AlastChanged&size=2&created=gte%3A2026-10-01&created=lt%3A2026-11-01", request.Call);
        Assert.Equal("Bearer pasted", request.Authorization);
        Assert.Equal("local.altinn.cloud:8000", request.Host);
        Assert.Equal("person:01017512345", Assert.Single(_identifiers));

        Assert.Contains("2 instances on this page.", body);
        Assert.Contains($"href=\"/Home/Instances/500000/{Running}?auth=token&amp;token=pasted\"", body);
        Assert.Contains($"href=\"/Home/Instances/512345/{Completed}?auth=token&amp;token=pasted\"", body);
        Assert.Contains("Task_1 (Utfylling)", body);
        Assert.Contains("ended 2026-10-25T09:30:00Z (EndEvent_1)", body);
        Assert.Contains("<span class=\"badge\">archived</span>", body);
        // The continuation token as Storage's next link has it (once decoded), with the rest of the query kept.
        Assert.Contains("id=\"next\" href=\"/Home/Instances/Results?auth=token&amp;token=Bearer%20pasted&amp;org=ttd", body);
        Assert.Contains("&amp;continuationToken=638%253B42\"", body);
        Assert.Contains("id=\"edit\"", body);
        // Nothing to link to: no inbox, no receipt, no registered app.
        Assert.DoesNotContain("af.local.altinn.cloud", body);
        Assert.DoesNotContain("/receipt/", body);
    }

    [Fact]
    public async Task AServiceOwnerTokenComesFromTheMaskinportenExchange()
    {
        await using var door = Door();
        var (status, body) = await Get(door, "/Home/Instances/Results?auth=org&orgNumber=991825827&org=ttd");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("GET /authentication/api/v1/exchange/maskinporten?test=true", Assert.Single(_authentication.Calls));
        Assert.Equal("Bearer org-token", Assert.Single(_storage.Requests).Authorization);
        Assert.Contains("As service owner 991825827", body);
    }

    [Fact]
    public async Task ATestUsersTokenComesFromTheSignIn()
    {
        await using var door = Door();
        var (status, body) = await Get(door, "/Home/Instances/Results?auth=user&userId=12345&partyId=500000&authenticationLevel=3&instanceOwner.partyId=500000");
        Assert.True(status == HttpStatusCode.OK, body);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Bearer user-token", Assert.Single(_storage.Requests).Authorization);
        Assert.StartsWith("GET /authentication/api/v1/authentication?goTo=", _authentication.Calls.First());
    }

    [Theory]
    [InlineData("/Home/Instances/Results?auth=user&userId=99", HttpStatusCode.NotFound, "No test user with userId 99.")]
    [InlineData("/Home/Instances/Results?auth=user&userId=12345&partyId=1", HttpStatusCode.BadRequest, "cannot act for party 1")]
    [InlineData("/Home/Instances/Results?auth=user", HttpStatusCode.BadRequest, "needs userId")]
    [InlineData("/Home/Instances/Results?auth=token", HttpStatusCode.BadRequest, "No token was pasted.")]
    [InlineData("/Home/Instances/Results?auth=root", HttpStatusCode.BadRequest, "auth must be none, user, org or token.")]
    public async Task ATokenThatCannotBeHadIsTheAnswer(string path, HttpStatusCode expected, string message)
    {
        await using var door = Door();
        var (status, body) = await Get(door, path);

        Assert.Equal(expected, status);
        Assert.Contains(WebUtility.HtmlEncode(message), body);
        Assert.Empty(_storage.Requests);
    }

    [Fact]
    public async Task StoragesRefusalIsShownWithItsStatus()
    {
        await using var door = Door();
        var (status, body) = await Get(door, "/Home/Instances/Results?auth=token&token=t&searchString=broken");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Storage answered 400", body);
        Assert.Contains("Org or AppId must be defined.", body);

        (status, body) = await Get(door, "/Home/Instances/Results");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Contains("As no token", body);
    }

    [Fact]
    public async Task WithoutStorageThePageSaysSo()
    {
        await using var door = new FrontDoor();
        var (status, body) = await Get(door, "/Home/Instances/Results?auth=token&token=t&org=ttd");
        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Contains("Could not reach Storage", body);
    }

    [Fact]
    public async Task TheInstancePageShowsFieldsDataElementsAndSimilarInstances()
    {
        await using var door = Door();
        var (status, body) = await Get(door, $"/Home/Instances/500000/{Running}?auth=org");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal($"GET /storage/api/v1/instances/500000/{Running}", _storage.Calls.Single());
        Assert.Contains("<th>process.currentTask.elementId</th><td class=\"wrap\">Task_1</td>", body);
        Assert.Contains("<th>instanceOwner.organisationNumber</th>", body);
        Assert.DoesNotContain("<th>data[0].id</th>", body);
        Assert.Contains($"href=\"/Home/Instances/500000/{Running}/data/{DataGuid}?auth=org\">Download</a>", body);
        // A service owner's links always name the org; "same app" and "same org and app" come out the same once.
        Assert.Contains("href=\"/Home/Instances?auth=org&amp;org=ttd&amp;instanceOwner.partyId=500000\">Same owner</a>", body);
        Assert.Contains("href=\"/Home/Instances?auth=org&amp;org=ttd&amp;appId=ttd%2Fapp\">Same app</a>", body);
        Assert.Contains("href=\"/Home/Instances?auth=org&amp;org=ttd&amp;appId=ttd%2Fapp&amp;instanceOwner.partyId=500000\">Same owner and app</a>", body);
        Assert.Contains("href=\"/Home/Instances?auth=org&amp;org=ttd\">Same org</a>", body);
        Assert.Contains("href=\"/Home/Instances?auth=org&amp;org=ttd&amp;appId=ttd%2Fapp&amp;process.currentTask=Task_1\">Same app, same task</a>", body);
        Assert.Contains("status.isArchived=false&amp;status.isSoftDeleted=false&amp;status.isHardDeleted=false\">Same app, same archived and deleted status</a>", body);
        Assert.Contains("None: the app is not registered", body);
    }

    [Fact]
    public async Task ATestUsersSimilarInstancesAlwaysNameTheOwner()
    {
        await using var door = Door();
        var (_, body) = await Get(door, $"/Home/Instances/512345/{Completed}?auth=token&token=t");
        Assert.Contains("process.endEvent=EndEvent_1\">Same app, same end event</a>", body);

        (_, body) = await Get(door, $"/Home/Instances/500000/{Running}?auth=user&userId=12345&partyId=500000");
        Assert.Contains("href=\"/Home/Instances?auth=user&amp;userId=12345&amp;partyId=500000&amp;appId=ttd%2Fapp&amp;instanceOwner.partyId=500000\">Same app</a>", body);
        // Same as "same app" for a user, so shown once.
        Assert.DoesNotContain(">Same owner and app</a>", body);
        Assert.Contains("A test user's query always names the owner.", body);
    }

    [Fact]
    public async Task TheLinksFollowWhatRuns()
    {
        await using (var door = Door("arbeidsflate", "dialogporten-adapter", "receipt"))
        {
            var (_, body) = await Get(door, $"/Home/Instances/512345/{Completed}?auth=token&token=t");
            Assert.Contains("href=\"http://af.local.altinn.cloud:8000/?party=urn%3Aaltinn%3Aperson%3Aidentifier-no%3A01017512345\">Inbox</a>", body);
            // The adapter's recorded dialog id wins over the derived one.
            Assert.Contains("href=\"http://af.local.altinn.cloud:8000/inbox/01937c7e-0000-7000-8000-000000000001\">Dialog</a>", body);
            Assert.Contains($"href=\"http://local.altinn.cloud:8000/receipt/512345/{Completed}\">Receipt</a>", body);

            (_, body) = await Get(door, $"/Home/Instances/500000/{Running}?auth=token&token=t");
            Assert.Contains("?party=urn%3Aaltinn%3Aorganization%3Aidentifier-no%3A897069650\">Inbox</a>", body);
            var derived = InstanceLinks.ToVersion7(Guid.Parse(Running), DateTimeOffset.Parse("2026-10-26T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
            Assert.Contains($"/inbox/{derived}\">Dialog</a>", body);
            // Not complete and the app is not registered: no link to it.
            Assert.DoesNotContain("#/instance/", body);

            door.Services.GetRequiredService<AppRoutes>().Set(new AppRoute("ttd", "app", "http://127.0.0.1:1", DateTimeOffset.UtcNow, null));
            (_, body) = await Get(door, $"/Home/Instances/500000/{Running}?auth=token&token=t");
            Assert.Contains($"href=\"http://local.altinn.cloud:8000/ttd/app/#/instance/500000/{Running}\">Open in the app</a>", body);
        }

        // The inbox without the adapter: no dialog; without the receipt group a completed instance goes to the app.
        await using (var door = Door("arbeidsflate"))
        {
            door.Services.GetRequiredService<AppRoutes>().Set(new AppRoute("ttd", "app", "http://127.0.0.1:1", DateTimeOffset.UtcNow, null));
            var (_, body) = await Get(door, $"/Home/Instances/512345/{Completed}?auth=token&token=t");
            Assert.Contains(">Inbox</a>", body);
            Assert.DoesNotContain(">Dialog</a>", body);
            Assert.Contains($"href=\"http://local.altinn.cloud:8000/ttd/app/#/instance/512345/{Completed}\">Receipt in the app</a>", body);
        }

        // The adapter without the inbox: neither inbox nor dialog.
        await using (var door = Door("dialogporten-adapter"))
        {
            var (_, body) = await Get(door, $"/Home/Instances/512345/{Completed}?auth=token&token=t");
            Assert.DoesNotContain("af.local.altinn.cloud", body);
            Assert.Contains("None: the app is not registered", body);
        }
    }

    [Fact]
    public async Task ADataElementDownloadsThroughTheProxyWithTheToken()
    {
        await using var door = Door();
        using var response = await door.Client().GetAsync($"/Home/Instances/500000/{Running}/data/{DataGuid}?auth=org", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("report.pdf", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("%PDF", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("Bearer org-token", Assert.Single(_storage.Requests).Authorization);

        using var missing = await door.Client().GetAsync($"/Home/Instances/500000/{Running}/data/{Guid.Empty}?auth=org", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public void TheDialogIdIsTheAdaptersUuidV7()
    {
        var guid = Guid.Parse(Running);
        var created = new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
        var dialogId = InstanceLinks.ToVersion7(guid, created);
        var text = dialogId.ToString("N");
        var source = guid.ToString("N");

        // 48 bits of Unix milliseconds, version 7, the RFC 4122 variant; the rest is the instance GUID's.
        Assert.Equal(created.ToUnixTimeMilliseconds().ToString("x12", System.Globalization.CultureInfo.InvariantCulture), text[..12]);
        Assert.Equal('7', text[12]);
        Assert.Equal(source[13..16], text[13..16]);
        Assert.Contains(text[16], "89ab");
        Assert.Equal(source[17..], text[17..]);
    }
}
