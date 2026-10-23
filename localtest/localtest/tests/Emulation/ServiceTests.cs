using System.Net;
using System.Text;
using System.Text.Json;

namespace Localtest.Tests.Emulation;

/// <summary>The emulated Register, Profile, Authorization, Access Management, Events, Notifications and Resource Registry.</summary>
public sealed class ServiceTests : IAsyncLifetime
{
    private MinimalFrontDoor _frontDoor = null!;
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _frontDoor = new MinimalFrontDoor();
        _client = _frontDoor.Client();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _frontDoor.DisposeAsync();
    }

    private async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == expected, $"{response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private async Task<JsonElement> Get(string path) => await Json(await _client.GetAsync(path, Ct));

    private async Task<JsonElement> Post(string path, string json, HttpStatusCode expected = HttpStatusCode.OK)
        => await Json(await _client.PostAsync(path, MinimalFrontDoor.Json(json), Ct), expected);

    [Fact]
    public async Task RegisterServesPartiesLookupsPartyListsAndUrnQueries()
    {
        using var response = await _client.GetAsync("/register/api/v1/parties/500000", Ct);
        Assert.Equal("register", Assert.Single(response.Headers.GetValues(Localtest.Emulation.Header)));
        var party = await Json(response);
        Assert.Equal("DDG Fitness", party.GetProperty("name").GetString());
        Assert.Equal(2, party.GetProperty("partyTypeName").GetInt32());
        Assert.Equal("897069644", party.GetProperty("organization").GetProperty("orgNumber").GetString());

        Assert.Equal(501337, (await Post("/register/api/v1/parties/lookup", """{"ssn":"01839012345"}""")).GetProperty("partyId").GetInt32());
        Assert.Equal(500000, (await Post("/register/api/v1/parties/lookup", """{"orgNo":"897069644"}""")).GetProperty("partyId").GetInt32());
        using var missing = await _client.PostAsync("/register/api/v1/parties/lookup", MinimalFrontDoor.Json("""{"ssn":"01017012345"}"""), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var list = await Post("/register/api/v1/parties/partylist?fetchSubUnits=true", "[500000, 501337]");
        Assert.Equal(3, list[0].GetProperty("childParties").GetArrayLength());
        Assert.Equal("Sophie Salt", list[1].GetProperty("person").GetProperty("name").GetString());

        Assert.Equal(501337, (await Post("/register/api/v1/apps/parties/query?fields=id,user.id", """{"data":["urn:altinn:person:identifier-no:01839012345"]}""")).GetProperty("data")[0].GetProperty("partyId").GetInt32());
        Assert.Equal(1337, (await Post("/register/api/v1/apps/parties/query?fields=id,user.id", """{"data":["urn:altinn:party:id:501337"]}""")).GetProperty("data")[0].GetProperty("user").GetProperty("userId").GetInt32());
        Assert.Equal("00000000-0000-0000-0000-000000500000", (await Post("/register/api/v1/access-management/parties/query", """{"data":["urn:altinn:organization:identifier-no:897069644"]}""")).GetProperty("data")[0].GetProperty("partyUuid").GetString());

        Assert.Equal("DDG Fitness Oslo", (await Get("/register/api/v1/organizations/897069660")).GetProperty("name").GetString());
        var users = await Get("/register/api/localtest/test-users");
        Assert.Equal(8, users.GetArrayLength());
    }

    [Fact]
    public async Task PersonLookupsMatchTheLastNameAndAnswer429AfterTwoFailures()
    {
        async Task<HttpStatusCode> Lookup(string ssn, string lastName)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/register/api/v1/persons");
            request.Headers.Add("X-Ai-NationalIdentityNumber", ssn);
            request.Headers.Add("X-Ai-LastName", Convert.ToBase64String(Encoding.UTF8.GetBytes(lastName)));
            request.Headers.Authorization = MinimalFrontDoor.Bearer(await _client.GetStringAsync("/Home/GetTestUserToken?userId=1001", Ct));
            using var response = await _client.SendAsync(request, Ct);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await Lookup("01839012345", "salt"));
        Assert.Equal(HttpStatusCode.NotFound, await Lookup("01839012345", "Pepper"));
        Assert.Equal(HttpStatusCode.NotFound, await Lookup("01017012345", "Salt"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Lookup("01839012345", "Salt"));
    }

    [Fact]
    public async Task ProfileServesUserProfilesWithTheirParty()
    {
        var sophie = await Get("/profile/api/v1/users/1337");
        Assert.Equal(501337, sophie.GetProperty("party").GetProperty("partyId").GetInt32());
        Assert.Equal("nn", sophie.GetProperty("profileSettingPreference").GetProperty("language").GetString());
        Assert.Equal(1, sophie.GetProperty("userType").GetInt32());
        Assert.Equal(1337, (await Post("/profile/api/v1/users", "\"01839012345\"")).GetProperty("userId").GetInt32());
        Assert.Equal(1337, (await Get("/profile/api/v1/users/byuuid/" + sophie.GetProperty("userUuid").GetString())).GetProperty("userId").GetInt32());
        Assert.Equal(2, (await Get("/profile/api/v1/users/1004")).GetProperty("userType").GetInt32());
        using var missing = await _client.GetAsync("/profile/api/v1/users/9", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task ThePdpPermitsEverythingWithOneResultPerRequestAndTheEchoedAttributes()
    {
        var single = await Post("/authorization/api/v1/decision", """
            {"request":{"accessSubject":[{"attribute":[{"attributeId":"urn:altinn:userid","value":"1337","dataType":"string","includeInResult":false}]}],
             "action":[{"attribute":[{"attributeId":"urn:oasis:names:tc:xacml:1.0:action:action-id","value":"write","dataType":"string","includeInResult":false}]}],
             "resource":[{"attribute":[{"attributeId":"urn:altinn:org","value":"ttd","dataType":"string"}]}],"multiRequests":null}}
            """);
        var result = Assert.Single(single.GetProperty("response").EnumerateArray());
        Assert.Equal("Permit", result.GetProperty("decision").GetString());
        Assert.False(result.TryGetProperty("obligations", out _));
        Assert.Equal(JsonValueKind.Array, result.GetProperty("category").ValueKind);

        // As app-lib's MultiDecisionHelper and Storage send it: actions and instances echoed per reference.
        var multi = await Post("/authorization/api/v1/decision", """
            {"request":{"accessSubject":[{"id":"s1","attribute":[{"attributeId":"urn:altinn:userid","value":"1337","dataType":"string"}]}],
             "action":[{"id":"a1","attribute":[{"attributeId":"urn:oasis:names:tc:xacml:1.0:action:action-id","value":"read","dataType":"string","includeInResult":true}]},
                       {"id":"a2","attribute":[{"attributeId":"urn:oasis:names:tc:xacml:1.0:action:action-id","value":"sign","dataType":"string","includeInResult":true}]}],
             "resource":[{"id":"r1","attribute":[{"attributeId":"urn:altinn:instance-id","value":"500000/1d9a","dataType":"string","includeInResult":true},{"attributeId":"urn:altinn:task","value":"Task_1","dataType":"string"}]}],
             "multiRequests":{"requestReference":[{"referenceId":["s1","a1","r1"]},{"referenceId":["s1","a2","r1"]}]}}}
            """);
        var results = multi.GetProperty("response").EnumerateArray().ToList();
        Assert.Equal(2, results.Count);
        string[] Echoed(JsonElement r) => [.. r.GetProperty("category").EnumerateArray().SelectMany(c => c.GetProperty("attribute").EnumerateArray()).Select(a => a.GetProperty("value").GetString()!)];
        Assert.Equal(["read", "500000/1d9a"], Echoed(results[0]));
        Assert.Equal(["sign", "500000/1d9a"], Echoed(results[1]));
        Assert.Equal("urn:oasis:names:tc:xacml:3.0:attribute-category:action", results[1].GetProperty("category")[0].GetProperty("categoryId").GetString());
        Assert.Equal("http://www.w3.org/2001/XMLSchema#string", results[1].GetProperty("category")[0].GetProperty("attribute")[0].GetProperty("dataType").GetString());

        Assert.Equal("Permit", (await Get("/authorization/api/localtest/decision?userId=1337&partyId=500005&org=ttd&app=testapp&action=read")).GetProperty("response")[0].GetProperty("decision").GetString());
    }

    [Fact]
    public async Task AuthorizationListsAndValidatesTheUsersParties()
    {
        var parties = await Get("/authorization/api/v1/parties?userId=1337");
        Assert.Equal(501337, parties[0].GetProperty("partyId").GetInt32());
        var ddg = parties.EnumerateArray().Single(p => p.GetProperty("partyId").GetInt32() == 500000);
        Assert.Equal(3, ddg.GetProperty("childParties").GetArrayLength());
        Assert.True((await Get("/authorization/api/v1/parties/500001/validate?userId=1337")).GetBoolean());
        Assert.False((await Get("/authorization/api/v1/parties/500005/validate?userId=1337")).GetBoolean());
        using var policy = await _client.PostAsync("/authorization/api/v1/policies?org=ttd&app=testapp", new StringContent("<Policy/>", Encoding.UTF8, "application/xml"), Ct);
        Assert.Equal(HttpStatusCode.OK, policy.StatusCode);
    }

    [Fact]
    public async Task AccessManagementAnswersTheAuthorizedPartiesAndEchoesDelegations()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/accessmanagement/api/v1/enduser/authorizedparties?includeRoles=true&includeSubParties=true");
        request.Headers.Authorization = MinimalFrontDoor.Bearer(await _client.GetStringAsync("/Home/GetTestUserToken?userId=1337", Ct));
        var parties = (await Json(await _client.SendAsync(request, Ct))).GetProperty("data");
        Assert.Equal("Person", parties[0].GetProperty("type").GetString());
        var ddg = parties.EnumerateArray().Single(p => p.GetProperty("partyId").GetInt32() == 500000);
        Assert.Equal("Organization", ddg.GetProperty("type").GetString());
        Assert.Equal(3, ddg.GetProperty("subunits").GetArrayLength());
        using var anonymous = await _client.GetAsync("/accessmanagement/api/v1/enduser/authorizedparties", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var delegation = await Post("/accessmanagement/api/v1/app/delegations/resource/app_ttd_testapp/instance/1d9a", """
            {"from":{"type":"urn:altinn:party:uuid","value":"00000000-0000-0000-0000-000000500000"},"to":{"type":"urn:altinn:party:uuid","value":"00000000-0000-0000-0000-000000501337"},
             "rights":[{"resource":[{"type":"urn:altinn:org","value":"ttd"}],"action":{"type":"urn:oasis:names:tc:xacml:1.0:action:action-id","value":"sign"}}]}
            """);
        Assert.Equal("Delegated", delegation.GetProperty("rights")[0].GetProperty("status").GetString());
        Assert.Equal("sign", delegation.GetProperty("rights")[0].GetProperty("action").GetProperty("value").GetString());
        Assert.Equal("1d9a", delegation.GetProperty("instanceId").GetString());
    }

    [Fact]
    public async Task EventsAreKeptBoundedAndReadBackOnTheComponentsInspectionPaths()
    {
        using var published = await _client.PostAsync("/events/api/v1/app", MinimalFrontDoor.Json("""
            {"id":null,"source":"http://local.altinn.cloud:8000/ttd/testapp/instances/500000/1d9a","specversion":"1.0","type":"app.instance.created","subject":"/party/500000"}
            """), Ct);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var id = await published.Content.ReadAsStringAsync(Ct);
        Assert.True(Guid.TryParse(id, out _), id);

        var events = await Get("/events/api/localtest/events?org=ttd&app=testapp");
        var stored = Assert.Single(events.GetProperty("items").EnumerateArray());
        Assert.Equal(id, stored.GetProperty("id").GetString());
        Assert.Equal("500000/1d9a", stored.GetProperty("instance").GetString());

        for (var i = 0; i < 1001; i++)
        {
            using var more = await _client.PostAsync("/events/api/v1/app", MinimalFrontDoor.Json($$"""{"source":"x","type":"t{{i}}","specversion":"1.0"}"""), Ct);
        }

        Assert.Equal("t1", (await Get("/events/api/localtest/events?limit=1")).GetProperty("items")[0].GetProperty("type").GetString());
        var subscription = await Post("/events/api/v1/subscriptions", """{"EndPoint":"http://local.altinn.cloud:8000/ttd/testapp/api/v1/eventsreceiver","SourceFilter":"http://local.altinn.cloud:8000/ttd/testapp","TypeFilter":"app.instance.process.completed"}""", HttpStatusCode.Created);
        Assert.Equal("/app/ttd/testapp", subscription.GetProperty("consumer").GetString());
        using var reset = await _client.DeleteAsync("/events/api/localtest/events", Ct);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(0, (await Get("/events/api/localtest/events")).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task NotificationsResolveRecipientsFromTheTestDataAndAnswerDelivered()
    {
        var email = await Post("/notifications/api/v1/orders/email", """
            {"subject":"Hei","body":"Skjema mottatt","sendersReference":"ref-1","recipients":[{"nationalIdentityNumber":"01839012345"},{"emailAddress":"direct@example.com"}],"contentType":"Plain"}
            """, HttpStatusCode.Accepted);
        var orderId = email.GetProperty("orderId").GetString();
        var sms = await Post("/notifications/api/v1/orders/sms", """{"senderNumber":"Altinn","body":"Hei","recipients":[{"organisationNumber":"897069660"}]}""", HttpStatusCode.Accepted);
        var future = await Post("/notifications/api/v1/future/orders", """
            {"sendersReference":"ref-2","recipient":{"recipientPerson":{"nationalIdentityNumber":"01839012345","channelSchema":4,"emailSettings":{"subject":"S","body":"B"},"smsSettings":{"body":"B"}}},
             "reminders":[{"delayDays":7,"recipient":{"recipientEmail":{"emailAddress":"reminder@example.com","emailSettings":{"subject":"R","body":"R"}}}}]}
            """, HttpStatusCode.Created);
        Assert.Equal(JsonValueKind.String, future.GetProperty("notification").GetProperty("shipmentId").ValueKind);
        Assert.Single(future.GetProperty("reminders").EnumerateArray());

        var messages = (await Get("/notifications/api/localtest/messages")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(
            ["1337@altinnstudiotestusers.com", "direct@example.com", "+4799500000", "1337@altinnstudiotestusers.com", "+4799001337", "reminder@example.com"],
            messages.Select(m => m.GetProperty("address").GetString()));
        Assert.Equal(2, (await Get("/notifications/api/localtest/messages?orderId=" + orderId)).GetProperty("items").GetArrayLength());
        Assert.Contains((await Get("/notifications/api/localtest/inboxes")).GetProperty("items").EnumerateArray(), i => i.GetProperty("address").GetString() == "+4799500000");
        Assert.Equal("Delivered", (await Get($"/notifications/api/v1/orders/{orderId}/notifications/email")).GetProperty("notifications")[0].GetProperty("sendStatus").GetProperty("status").GetString());
        Assert.Equal("Completed", (await Get($"/notifications/api/v1/orders/{sms.GetProperty("orderId").GetString()}/status")).GetProperty("processingStatus").GetProperty("status").GetString());

        using var cancel = await _client.PutAsync($"/notifications/api/v1/orders/{orderId}/cancel", null, Ct);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("Cancelled", (await Get($"/notifications/api/v1/orders/{orderId}/status")).GetProperty("processingStatus").GetProperty("status").GetString());
        using var reset = await _client.DeleteAsync("/notifications/api/localtest/messages", Ct);
        Assert.Equal(0, (await Get("/notifications/api/localtest/messages")).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task UnknownRoutesAndTheResourceRegistryAnswer501NamingTheMode()
    {
        using var unknown = await _client.GetAsync("/register/api/v2/internal/parties", Ct);
        var problem = await Json(unknown, HttpStatusCode.NotImplemented);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
        Assert.Contains("/register/api/v2/internal/parties", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal("minimal", problem.GetProperty("mode").GetString());
        Assert.Equal("register", Assert.Single(unknown.Headers.GetValues(Localtest.Emulation.Header)));

        using var registry = await _client.GetAsync("/resourceregistry/api/v1/resource/app_ttd_testapp", Ct);
        var registryProblem = await Json(registry, HttpStatusCode.NotImplemented);
        Assert.Contains("minimal mode", registryProblem.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Equal("resource-registry", Assert.Single(registry.Headers.GetValues(Localtest.Emulation.Header)));

        // Storage is not emulated: without its address it is "not running", as in the default stack, and unmarked.
        using var storage = await _client.GetAsync("/storage/api/v1/applications", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, storage.StatusCode);
        Assert.False(storage.Headers.Contains(Localtest.Emulation.Header));
    }

    [Fact]
    public async Task TheRoutesApiReportsTheEmulatedServices()
    {
        var routes = (await Get("/localtest/api/routes")).GetProperty("platform").EnumerateArray()
            .ToDictionary(r => r.GetProperty("service").GetString()!, r => r.GetProperty("address").GetString());
        Assert.Equal("emulated", routes["register"]);
        Assert.Equal("emulated", routes["pdf3"]);
        Assert.Equal("emulated", routes["resource-registry"]);
        Assert.Null(routes["storage"]);
        Assert.Null(routes["correspondence"]);
    }
}
