using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Core.A2.SblProfile;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.Models;
using Altinn.Register.TestUtils.TestData;
using Altinn.Urn;

namespace Altinn.Register.IntegrationTests.Controllers;

public class UsersControllerTests
    : IntegrationTestBase
{
    private const string EndpointUrl = "register/api/v2/internal/parties/self-identified";

    [Fact]
    public async Task A2_GetOrCreate_IdPortenEmail_LookupHit_ReturnsExisting()
    {
        SetSource(TestApiSource.A2);

        const string ExternalIdentity = "urn:altinn:person:idporten-email:dXNlckBleGFtcGxlLmNvbQ";

        var existing = new SblUserProfile
        {
            UserId = 4242,
            UserUuid = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            UserName = "epost:user@example.com",
            PartyId = 50001,
            ExternalIdentity = ExternalIdentity,
            UserType = 2,
        };

        FakeHttpHandlers.For<ISblProfileBridgeClient>()
            .Expect(HttpMethod.Post, "/profile/api/users/")
            .Respond(() => JsonContent.Create(existing, options: JsonOptions));

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
            .WithPlatformToken("unittest");
        request.Content = JsonContent.Create(new SelfIdentifiedUserCreateRequest
        {
            SelfIdentifiedUserType = SelfIdentifiedUserType.IdPortenEmail,
            ExternalIdentity = ExternalIdentity,
            UserName = "user@example.com",
            Email = "user@example.com",
        });

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var body = await response.ShouldHaveJsonContent<SelfIdentifiedUser>();

        body.ShouldNotBeNull();
        body.User.Value!.UserId.Value.ShouldBe(4242u);
        body.PartyId.Value.ShouldBe(50001u);
        body.Uuid.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        body.User.Value.Username.Value.ShouldBe("epost:user@example.com");
        body.SelfIdentifiedUserType.Value.Value.ShouldBe(SelfIdentifiedUserType.IdPortenEmail);
        body.Email.Value.ShouldBe("user@example.com");
    }

    [Fact]
    public async Task GetOrCreate_IdPortenEmail_LookupHit_ReturnsExisting()
    {
        const string ExternalIdentity = "urn:altinn:person:idporten-email:dXNlckBleGFtcGxlLmNvbQ";
        const string Email = "user@example.com";

        var existing = await Setup((uow, ct) => uow.CreateSelfIdentifiedUser(
            type: SelfIdentifiedUserType.IdPortenEmail,
            email: Email,
            name: $"email:{Email}",
            cancellationToken: ct));

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
            .WithPlatformToken("unittest");
        request.Content = JsonContent.Create(new SelfIdentifiedUserCreateRequest
        {
            SelfIdentifiedUserType = SelfIdentifiedUserType.IdPortenEmail,
            ExternalIdentity = ExternalIdentity,
            UserName = Email,
            Email = Email,
        });

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var body = await response.ShouldHaveJsonContent<SelfIdentifiedUser>();

        var existingUserId = existing.UserIds.CurrentValue;
        var existingUsername = existing.Usernames.CurrentValue;
        body.ShouldNotBeNull();
        body.User.Value!.UserId.Value.ShouldBe(existingUserId.Value);
        body.PartyId.Value.ShouldBe(existing.PartyId.Value);
        body.Uuid.ShouldBe(existing.PartyUuid.Value);
        body.User.Value.Username.Value.ShouldBe(existingUsername.Value);
        body.SelfIdentifiedUserType.Value.Value.ShouldBe(SelfIdentifiedUserType.IdPortenEmail);
        body.Email.Value.ShouldBe(Email);
        body.ExternalUrn.IsNull.ShouldBeFalse();
    }

    [Fact]
    public async Task A2_GetOrCreate_IdPortenEmail_LookupMiss_CreatesUser()
    {
        SetSource(TestApiSource.A2);

        // Caller builds the bridge-shape URN and chooses the username (see altinn-authentication's
        // selfregistered-email path: `epost:<email>` is the convention).
        const string ExternalIdentity = "urn:altinn:person:idporten-email:bmV3QGV4YW1wbGUuY29t";
        const string UserName = "epost:new@example.com";

        FakeHttpHandlers.For<ISblProfileBridgeClient>()
            .Expect(HttpMethod.Post, "/profile/api/users/")
            .Respond(HttpStatusCode.NotFound);

        var created = new SblUserProfile
        {
            UserId = 9001,
            UserUuid = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            UserName = UserName,
            PartyId = 50002,
            ExternalIdentity = ExternalIdentity,
            UserType = 2,
        };

        FakeHttpHandlers.For<ISblProfileBridgeClient>()
            .Expect(HttpMethod.Post, "/profile/api/users/create/")
            .Respond(() => JsonContent.Create(created, options: JsonOptions));

        ExpectAccessManagementSelfIdentifiedUserCreate(
            partyUuid: created.UserUuid.Value,
            partyId: (uint)created.PartyId,
            userId: (uint)created.UserId,
            entityVariantType: "SI_EMAIL",
            displayName: "new@example.com",
            emailIdentifier: "new@example.com",
            externalUrn: PartyExternalRefUrn.IDPortenEmail.Create(UrnEncoded.Create("new@example.com")));

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
            .WithPlatformToken("unittest");
        request.Content = JsonContent.Create(new SelfIdentifiedUserCreateRequest
        {
            SelfIdentifiedUserType = SelfIdentifiedUserType.IdPortenEmail,
            ExternalIdentity = ExternalIdentity,
            UserName = UserName,
            Email = "new@example.com",
        });

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var body = await response.ShouldHaveJsonContent<SelfIdentifiedUser>();

        body.ShouldNotBeNull();
        body.User.Value!.UserId.Value.ShouldBe(9001u);
    }

    [Fact]
    public async Task GetOrCreate_IdPortenEmail_LookupMiss_CreatesUser()
    {
        const string ExternalIdentity = "urn:altinn:person:idporten-email:bmV3QGV4YW1wbGUuY29t";
        const string UserName = "epost:new@example.com";
        const string Email = "new@example.com";

        ExpectAccessManagementSelfIdentifiedUserCreate(
            partyUuid: FieldValue.Unset,
            partyId: FieldValue.Unset,
            userId: FieldValue.Unset,
            entityVariantType: "SI_EMAIL",
            displayName: Email,
            emailIdentifier: Email,
            externalUrn: PartyExternalRefUrn.IDPortenEmail.Create(UrnEncoded.Create(Email)));

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
            .WithPlatformToken("unittest");
        request.Content = JsonContent.Create(new SelfIdentifiedUserCreateRequest
        {
            SelfIdentifiedUserType = SelfIdentifiedUserType.IdPortenEmail,
            ExternalIdentity = ExternalIdentity,
            UserName = UserName,
            Email = Email,
        });

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var body = await response.ShouldHaveJsonContent<SelfIdentifiedUser>();

        body.ShouldNotBeNull();
        body.User.Value!.UserId.Value.ShouldBe(body.PartyId.Value);
        body.User.Value.Username.IsNull.ShouldBeTrue();
        body.SelfIdentifiedUserType.Value.Value.ShouldBe(SelfIdentifiedUserType.IdPortenEmail);
        body.Email.Value.ShouldBe(Email);
        body.ExternalUrn.IsNull.ShouldBeFalse();
    }

    [Fact]
    public async Task GetOrCreate_IdPortenEmail_LookupMiss_WithUppercaseEmail_StoresLowercaseEmailAndExternalUrn()
    {
        const string UppercaseEmail = "UPPER@example.com";
        const string LowercaseEmail = "upper@example.com";
        var expectedExternalUrn = PartyExternalRefUrn.IDPortenEmail.Create(UrnEncoded.Create(LowercaseEmail));

        ExpectAccessManagementSelfIdentifiedUserCreate(
            partyUuid: FieldValue.Unset,
            partyId: FieldValue.Unset,
            userId: FieldValue.Unset,
            entityVariantType: "SI_EMAIL",
            displayName: LowercaseEmail,
            emailIdentifier: LowercaseEmail,
            externalUrn: expectedExternalUrn);

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
            .WithPlatformToken("unittest");
        request.Content = JsonContent.Create(new SelfIdentifiedUserCreateRequest
        {
            SelfIdentifiedUserType = SelfIdentifiedUserType.IdPortenEmail,
            ExternalIdentity = expectedExternalUrn.ToString(),
            UserName = $"epost:{LowercaseEmail}",
            Email = UppercaseEmail,
        });

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var body = await response.ShouldHaveJsonContent<SelfIdentifiedUser>();

        body.ShouldNotBeNull();
        body.Email.Value.ShouldBe(LowercaseEmail);
        body.ExternalUrn.Value.ShouldBe(expectedExternalUrn);

        var stored = await Check(async (uow, ct) =>
            await uow.GetPartyPersistence()
                .GetPartyById(body.Uuid, PartyFieldIncludes.Party | PartyFieldIncludes.SelfIdentifiedUser, ct)
                .FirstAsync(ct));

        var storedSelfIdentifiedUser = stored.ShouldBeOfType<SelfIdentifiedUserRecord>();
        storedSelfIdentifiedUser.Email.Value.ShouldBe(LowercaseEmail);
        storedSelfIdentifiedUser.ExternalUrn.Value.ShouldBe(expectedExternalUrn);
    }

    [Fact]
    public async Task A2_GetOrCreate_Educational_LookupMiss_CreatesUser_WithNullExternalUrn()
    {
        SetSource(TestApiSource.A2);

        // Matches altinn-authentication's uidp-anonym provider (see appsettings.test.json):
        // Iss is the provider config key, ExternalSubject is a SHA-256 hex hash, UserName is
        // `uidp_` + hash segment + random suffix (see existing_uidpuser.json). The caller
        // concatenates these into a single ExternalIdentity before calling register.
        const string ExternalIdentity = "uidp-anonym:66a633c43ef2f656978f957532ce6d0de6f5e13f1e0618b37b4b2a70573e5551";
        const string UidpUserName = "uidp_newhash99x7";

        FakeHttpHandlers.For<ISblProfileBridgeClient>()
            .Expect(HttpMethod.Post, "/profile/api/users/")
            .Respond(HttpStatusCode.NotFound);

        var created = new SblUserProfile
        {
            UserId = 11011,
            UserUuid = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            UserName = UidpUserName,
            PartyId = 50004,
            ExternalIdentity = ExternalIdentity,
            UserType = 2,
        };

        FakeHttpHandlers.For<ISblProfileBridgeClient>()
            .Expect(HttpMethod.Post, "/profile/api/users/create/")
            .Respond(() => JsonContent.Create(created, options: JsonOptions));

        ExpectAccessManagementSelfIdentifiedUserCreate(
            partyUuid: created.UserUuid.Value,
            partyId: (uint)created.PartyId,
            userId: (uint)created.UserId,
            entityVariantType: "SI_EDU",
            displayName: UidpUserName,
            emailIdentifier: null,
            externalUrn: null);

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
            .WithPlatformToken("unittest");
        request.Content = JsonContent.Create(new SelfIdentifiedUserCreateRequest
        {
            SelfIdentifiedUserType = SelfIdentifiedUserType.Educational,
            ExternalIdentity = ExternalIdentity,
            UserName = UidpUserName,
        });

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var body = await response.ShouldHaveJsonContent<SelfIdentifiedUser>();

        body.ShouldNotBeNull();
        body.User.Value!.UserId.Value.ShouldBe(11011u);
        body.PartyId.Value.ShouldBe(50004u);
        body.Uuid.ShouldBe(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        body.SelfIdentifiedUserType.Value.Value.ShouldBe(SelfIdentifiedUserType.Educational);
        body.ExternalUrn.IsNull.ShouldBeTrue();
    }

    [Fact]
    public async Task GetOrCreate_Educational_LookupMiss_CreatesUser_WithNullExternalUrn()
    {
        const string ExternalIdentity = "uidp-anonym:66a633c43ef2f656978f957532ce6d0de6f5e13f1e0618b37b4b2a70573e5551";
        const string UidpUserName = "uidp_newhash99x7";

        ExpectAccessManagementSelfIdentifiedUserCreate(
            partyUuid: FieldValue.Unset,
            partyId: FieldValue.Unset,
            userId: FieldValue.Unset,
            entityVariantType: "SI_EDU",
            displayName: UidpUserName,
            emailIdentifier: null,
            externalUrn: null);

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
            .WithPlatformToken("unittest");
        request.Content = JsonContent.Create(new SelfIdentifiedUserCreateRequest
        {
            SelfIdentifiedUserType = SelfIdentifiedUserType.Educational,
            ExternalIdentity = ExternalIdentity,
            UserName = UidpUserName,
        });

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var body = await response.ShouldHaveJsonContent<SelfIdentifiedUser>();

        body.ShouldNotBeNull();
        body.User.Value!.UserId.Value.ShouldBe(body.PartyId.Value);
        body.User.Value.Username.IsNull.ShouldBeTrue();
        body.Uuid.ShouldNotBe(Guid.Empty);
        body.DisplayName.Value.ShouldBe(UidpUserName);
        body.SelfIdentifiedUserType.Value.Value.ShouldBe(SelfIdentifiedUserType.Educational);
        body.ExternalUrn.IsNull.ShouldBeTrue();
        body.Email.IsNull.ShouldBeTrue();
    }

    [Fact]
    public async Task A2_GetOrCreate_Educational_LookupHit_ReturnsExisting_WithNullExternalUrn()
    {
        SetSource(TestApiSource.A2);

        // Existing uidp/edu user — same shape as altinn-authentication's existing_uidpuser.json scenario.
        const string ExternalIdentity = "uidp-anonym:33a633c47ef2f656978f957532ce6d0de6f5e13f1e0618b37b4b2a70573e5551";
        const string UidpUserName = "uidp_ej2krar0cl833";

        var existing = new SblUserProfile
        {
            UserId = 11012,
            UserUuid = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            UserName = UidpUserName,
            PartyId = 50005,
            ExternalIdentity = ExternalIdentity,
            UserType = 2,
        };

        FakeHttpHandlers.For<ISblProfileBridgeClient>()
            .Expect(HttpMethod.Post, "/profile/api/users/")
            .Respond(() => JsonContent.Create(existing, options: JsonOptions));

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
            .WithPlatformToken("unittest");
        request.Content = JsonContent.Create(new SelfIdentifiedUserCreateRequest
        {
            SelfIdentifiedUserType = SelfIdentifiedUserType.Educational,
            ExternalIdentity = ExternalIdentity,
            UserName = UidpUserName,
        });

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var body = await response.ShouldHaveJsonContent<SelfIdentifiedUser>();

        body.ShouldNotBeNull();
        body.User.Value!.UserId.Value.ShouldBe(11012u);
        body.SelfIdentifiedUserType.Value.Value.ShouldBe(SelfIdentifiedUserType.Educational);
        body.ExternalUrn.IsNull.ShouldBeTrue();
    }

    [Fact]
    public async Task GetOrCreate_Educational_LookupHit_ReturnsExisting_WithNullExternalUrn()
    {
        const string ExternalIdentity = "uidp-anonym:33a633c47ef2f656978f957532ce6d0de6f5e13f1e0618b37b4b2a70573e5551";
        const string UidpUserName = "uidp_ej2krar0cl833";

        var existing = await Setup((uow, ct) => uow.CreateSelfIdentifiedUser(
            type: SelfIdentifiedUserType.Educational,
            extRef: ExternalIdentity,
            name: UidpUserName,
            cancellationToken: ct));

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
            .WithPlatformToken("unittest");
        request.Content = JsonContent.Create(new SelfIdentifiedUserCreateRequest
        {
            SelfIdentifiedUserType = SelfIdentifiedUserType.Educational,
            ExternalIdentity = ExternalIdentity,
            UserName = UidpUserName,
        });

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var body = await response.ShouldHaveJsonContent<SelfIdentifiedUser>();

        var existingUserId = existing.UserIds.CurrentValue;
        var existingUsername = existing.Usernames.CurrentValue;
        body.ShouldNotBeNull();
        body.User.Value!.UserId.Value.ShouldBe(existingUserId.Value);
        body.PartyId.Value.ShouldBe(existing.PartyId.Value);
        body.Uuid.ShouldBe(existing.PartyUuid.Value);
        body.User.Value.Username.Value.ShouldBe(existingUsername.Value);
        body.DisplayName.Value.ShouldBe(UidpUserName);
        body.SelfIdentifiedUserType.Value.Value.ShouldBe(SelfIdentifiedUserType.Educational);
        body.ExternalUrn.IsNull.ShouldBeTrue();
        body.Email.IsNull.ShouldBeTrue();
    }

    [Theory]
    [CombinatorialData]
    public async Task GetOrCreate_MissingExternalIdentity_ReturnsBadRequest(TestApiSource source)
    {
        SetSource(source);

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
            .WithPlatformToken("unittest");
        request.Content = JsonContent.Create(new SelfIdentifiedUserCreateRequest
        {
            SelfIdentifiedUserType = SelfIdentifiedUserType.IdPortenEmail,
            UserName = "user@example.com",
            Email = "user@example.com",
        });

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        var problem = await response.ShouldHaveJsonContent<AltinnValidationProblemDetails>();
        problem.ErrorCode.ShouldBe(StdProblemDescriptors.ErrorCodes.ValidationError);
        problem.Errors.ShouldContain(e => e.ErrorCode == StdValidationErrors.Required.ErrorCode && e.Paths.Contains("/externalIdentity"));
    }

    [Theory]
    [CombinatorialData]
    public async Task GetOrCreate_MissingUserName_ReturnsBadRequest(TestApiSource source)
    {
        SetSource(source);

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
            .WithPlatformToken("unittest");
        request.Content = JsonContent.Create(new SelfIdentifiedUserCreateRequest
        {
            SelfIdentifiedUserType = SelfIdentifiedUserType.IdPortenEmail,
            ExternalIdentity = "urn:altinn:person:idporten-email:dXNlckBleGFtcGxlLmNvbQ",
            Email = "user@example.com",
        });

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        var problem = await response.ShouldHaveJsonContent<AltinnValidationProblemDetails>();
        problem.ErrorCode.ShouldBe(StdProblemDescriptors.ErrorCodes.ValidationError);
        problem.Errors.ShouldContain(e => e.ErrorCode == StdValidationErrors.Required.ErrorCode && e.Paths.Contains("/userName"));
    }

    [Theory]
    [CombinatorialData]
    public async Task GetOrCreate_IdPortenEmail_MissingEmail_ReturnsBadRequest(TestApiSource source)
    {
        SetSource(source);

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
            .WithPlatformToken("unittest");
        request.Content = JsonContent.Create(new SelfIdentifiedUserCreateRequest
        {
            SelfIdentifiedUserType = SelfIdentifiedUserType.IdPortenEmail,
            ExternalIdentity = "urn:altinn:person:idporten-email:dXNlckBleGFtcGxlLmNvbQ",
            UserName = "user@example.com",
        });

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        var problem = await response.ShouldHaveJsonContent<AltinnValidationProblemDetails>();
        problem.ErrorCode.ShouldBe(StdProblemDescriptors.ErrorCodes.ValidationError);
        problem.Errors.ShouldContain(e => e.ErrorCode == StdValidationErrors.Required.ErrorCode && e.Paths.Contains("/email"));
    }

    [Fact]
    public async Task A2_GetOrCreate_BridgeUnavailable_ReturnsProblem()
    {
        SetSource(TestApiSource.A2);

        FakeHttpHandlers.For<ISblProfileBridgeClient>()
            .Expect(HttpMethod.Post, "/profile/api/users/")
            .Respond(HttpStatusCode.InternalServerError);

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
            .WithPlatformToken("unittest");
        request.Content = JsonContent.Create(new SelfIdentifiedUserCreateRequest
        {
            SelfIdentifiedUserType = SelfIdentifiedUserType.IdPortenEmail,
            ExternalIdentity = "urn:altinn:person:idporten-email:dXNlckBleGFtcGxlLmNvbQ",
            UserName = "user@example.com",
            Email = "user@example.com",
        });

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.InternalServerError);
        var problem = await response.ShouldHaveJsonContent<AltinnProblemDetails>();
        problem.ErrorCode.ShouldBe(Problems.PartyFetchFailed.ErrorCode);
    }

    [Theory]
    [CombinatorialData]
    public async Task GetOrCreate_Unauthorized_ReturnsUnauthorized(TestApiSource source)
    {
        SetSource(source);

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl);
        request.Headers.Authorization = new("Bearer", "bogus");
        request.Content = JsonContent.Create(new SelfIdentifiedUserCreateRequest
        {
            SelfIdentifiedUserType = SelfIdentifiedUserType.IdPortenEmail,
            ExternalIdentity = "urn:altinn:person:idporten-email:dXNlckBleGFtcGxlLmNvbQ",
            UserName = "user@example.com",
            Email = "user@example.com",
        });

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        await response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }

    private void SetSource(TestApiSource source)
    {
        var value = source == TestApiSource.DB ? "true" : "false";
        Configuration["Altinn:register:Party:CreatePartyId"] = value;
    }

    private void ExpectAccessManagementSelfIdentifiedUserCreate(
        FieldValue<Guid> partyUuid,
        FieldValue<uint> partyId,
        FieldValue<uint> userId,
        string entityVariantType,
        string displayName,
        string? emailIdentifier,
        PartyExternalRefUrn? externalUrn)
    {
        Guid? actualPartyUuid = null;

        FakeHttpHandlers.For<TempWorkarounds.AccessManagementClient>()
            .Expect(HttpMethod.Post, "/api/v1/internal/party")
            .Respond(async (FakeHttpRequestMessage request, CancellationToken cancellationToken) =>
            {
                request.Content.ShouldNotBeNull();

                using var json = await request.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
                var root = json!.RootElement;

                actualPartyUuid = root.GetProperty("PartyUuid").GetGuid();
                actualPartyUuid.Value.ShouldNotBe(Guid.Empty);
                if (partyUuid.HasValue)
                {
                    actualPartyUuid.Value.ShouldBe(partyUuid.Value);
                }

                var actualPartyId = root.GetProperty("PartyId").GetUInt32();
                actualPartyId.ShouldBeGreaterThan(0u);
                if (partyId.HasValue)
                {
                    actualPartyId.ShouldBe(partyId.Value);
                }

                var actualUserId = root.GetProperty("UserId").GetUInt32();
                actualUserId.ShouldBeGreaterThan(0u);
                if (userId.HasValue)
                {
                    actualUserId.ShouldBe(userId.Value);
                }

                root.GetProperty("EntityType").GetString().ShouldBe("Selvidentifisert");
                root.GetProperty("EntityVariantType").GetString().ShouldBe(entityVariantType);
                root.GetProperty("DisplayName").GetString().ShouldBe(displayName);
                root.GetProperty("EmailIdentifier").GetString().ShouldBe(emailIdentifier);
                root.GetProperty("ExternalUrn").GetString().ShouldBe(externalUrn?.ToString());

                return HttpStatusCode.OK;
            });

        FakeHttpHandlers.For<TempWorkarounds.AccessManagementClient>()
            .Expect(HttpMethod.Post, "/api/v1/internal/connections/selfidentifiedusers")
            .Respond((FakeHttpRequestMessage request) =>
            {
                var pathAndQuery = request.RequestUri?.PathAndQuery;
                pathAndQuery.ShouldEndWith($"/api/v1/internal/connections/selfidentifiedusers?from={actualPartyUuid}&to={actualPartyUuid}");

                return HttpStatusCode.OK;
            });
    }
}
